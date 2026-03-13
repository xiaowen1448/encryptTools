using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace EncryptTools
{
    /// <summary>密钥缓存项，避免静态初始化时依赖 ValueTuple 导致类型初始化异常。</summary>
    internal sealed class KeyCacheEntry
    {
        public string? Password { get; set; }
        public byte[]? Salt { get; set; }
        public int Iterations { get; set; }
        public int KeySize { get; set; }
        public byte[]? Key { get; set; }
    }

    /// <summary>缓冲区池接口，便于在无法加载 ArrayPool 时使用简单实现。</summary>
    internal interface IBufferPool
    {
        byte[] Rent(int minimumLength);
        void Return(byte[] array);
    }

    internal sealed class ArrayPoolAdapter : IBufferPool
    {
        private readonly ArrayPool<byte> _pool;
        public ArrayPoolAdapter(ArrayPool<byte> pool) { _pool = pool; }
        public byte[] Rent(int minimumLength) => _pool.Rent(minimumLength);
        public void Return(byte[] array) { _pool.Return(array); }
    }

    internal sealed class SimpleBufferPool : IBufferPool
    {
        public byte[] Rent(int minimumLength) => new byte[minimumLength];
        public void Return(byte[] array) { }
    }

    public class CryptoService
    {
        /// <summary>固定 16 字节文件头：Magic(8) + Version(1) + EncryptType(1) + Reserved(6)。</summary>
        private static readonly byte[] HeaderMagic = System.Text.Encoding.ASCII.GetBytes("WXENC001");
        private const int HeaderSize = 16;
        private const byte HeaderVersion = 1;
        /// <summary>加密类型一 = AES-CBC，加密类型二 = AES-GCM。</summary>
        private const byte EncryptTypeCbc = 1;
        private const byte EncryptTypeGcm = 2;

        private const int BufferSize = 4 * 1024 * 1024; // 4MB缓冲区

        // 延迟初始化，避免类型初始化时加载 System.Buffers 或 ValueTuple 导致异常
        private static readonly Lazy<ThreadLocal<KeyCacheEntry>> KeyCacheLazy =
            new Lazy<ThreadLocal<KeyCacheEntry>>(() => new ThreadLocal<KeyCacheEntry>());

        private static readonly Lazy<IBufferPool> BufferPoolLazy = new Lazy<IBufferPool>(() =>
        {
            try
            {
                return new ArrayPoolAdapter(ArrayPool<byte>.Shared);
            }
            catch
            {
                return new SimpleBufferPool();
            }
        });

        private static ThreadLocal<KeyCacheEntry> KeyCache => KeyCacheLazy.Value;
        private static IBufferPool GetBufferPool() => BufferPoolLazy.Value;

        public async Task EncryptFileAsync(
            string inputPath,
            string outputPath,
            CryptoAlgorithm algorithm,
            string password,
            int iterations,
            int aesKeySizeBits,
            IProgress<long>? progress,
            CancellationToken ct)
        {
            if (algorithm != CryptoAlgorithm.AesCbc && algorithm != CryptoAlgorithm.AesGcm)
                throw new NotSupportedException("本格式仅支持加密类型一(AES-CBC)与加密类型二(AES-GCM)。");

            var salt = RandomBytes(16);
            byte encryptType = algorithm == CryptoAlgorithm.AesGcm ? EncryptTypeGcm : EncryptTypeCbc;

            var fileOptions = FileOptions.SequentialScan | FileOptions.WriteThrough;
            using var outFs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, fileOptions);
            using var bw = new BinaryWriter(outFs);

            // 固定 16 字节头：Magic(8) + Version(1) + EncryptType(1) + Reserved(6)
            bw.Write(HeaderMagic);
            bw.Write(HeaderVersion);
            bw.Write(encryptType);
            for (int i = 0; i < 6; i++) bw.Write((byte)0);

            bw.Write(iterations);
            bw.Write(salt.Length);
            bw.Write(salt);
            var keySizeToWrite = aesKeySizeBits;
            bw.Write(keySizeToWrite);

            var originalName = Path.GetFileName(inputPath);
            var nameBytes = System.Text.Encoding.UTF8.GetBytes(originalName);
            bw.Write(nameBytes.Length);
            bw.Write(nameBytes);

            switch (algorithm)
            {
                case CryptoAlgorithm.AesCbc:
                    await EncryptAesCbcAsync(inputPath, outFs, password, salt, iterations, aesKeySizeBits, progress, ct);
                    break;
                case CryptoAlgorithm.AesGcm:
#if NET48
                    throw new NotSupportedException(RuntimeHelper.GetAesGcmRequirementMessage());
#else
                    await EncryptAesGcmAsync(inputPath, outFs, password, salt, iterations, aesKeySizeBits, progress, ct);
                    break;
#endif
                case CryptoAlgorithm.TripleDes:
                    await EncryptTripleDesAsync(inputPath, outFs, password, salt, iterations, progress, ct);
                    break;
                case CryptoAlgorithm.Xor:
                    await EncryptXorAsync(inputPath, outFs, password, salt, iterations, progress, ct);
                    break;
                default:
                    throw new NotSupportedException("不支持的算法");
            }
        }

        public class DecryptResult
        {
            public string? OriginalFileName { get; set; }
        }

        /// <summary>
        /// 通过文件头 Magic 判断是否为合法加密文件（WXENC001）。不依赖扩展名。
        /// </summary>
        public static bool IsWxEncryptedFile(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16);
                if (fs.Length < HeaderSize) return false;
                var buf = new byte[HeaderMagic.Length];
                if (fs.Read(buf, 0, buf.Length) != buf.Length) return false;
                for (int i = 0; i < HeaderMagic.Length; i++)
                    if (buf[i] != HeaderMagic[i]) return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// 仅读取加密文件头（16 字节 Magic + 后续元数据），通过文件头标识判断是否为合法加密文件，返回算法和原始文件名。
        /// </summary>
        public static (CryptoAlgorithm Algorithm, string? OriginalFileName) PeekEncryptedFileInfo(string inputPath)
        {
            using var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 512);
            using var br = new BinaryReader(fs);
            byte[] header = br.ReadBytes(HeaderSize);
            if (header.Length < HeaderSize)
                throw new InvalidDataException("不是有效加密文件");
            for (int i = 0; i < HeaderMagic.Length; i++)
                if (header[i] != HeaderMagic[i])
                    throw new InvalidDataException("不是有效加密文件");
            byte encryptType = header[9];
            CryptoAlgorithm alg = encryptType == EncryptTypeGcm ? CryptoAlgorithm.AesGcm : CryptoAlgorithm.AesCbc;
            if (encryptType != EncryptTypeCbc && encryptType != EncryptTypeGcm)
                throw new InvalidDataException("解密类型未知，跳过文件");

            br.ReadInt32(); // iterations
            int saltLen = br.ReadInt32();
            if (saltLen < 0 || saltLen > 256) throw new InvalidDataException("文件头损坏");
            br.ReadBytes(saltLen);
            br.ReadInt32(); // keySizeBits
            int nameLen = br.ReadInt32();
            string? originalFileName = null;
            if (nameLen > 0 && nameLen < 4096)
                originalFileName = System.Text.Encoding.UTF8.GetString(br.ReadBytes(nameLen));
            return (alg, originalFileName);
        }

        public async Task<DecryptResult> DecryptFileAsync(
            string inputPath,
            string outputPath,
            string password,
            IProgress<long>? progress,
            CancellationToken ct)
        {
            var fileOptions = FileOptions.SequentialScan;
            using var inFs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, fileOptions);
            using var br = new BinaryReader(inFs);
            byte[] header = br.ReadBytes(HeaderSize);
            if (header.Length < HeaderSize)
                throw new InvalidDataException("不是有效加密文件");
            for (int i = 0; i < HeaderMagic.Length; i++)
                if (header[i] != HeaderMagic[i])
                    throw new InvalidDataException("不是有效加密文件");
            byte encryptType = header[9];
            CryptoAlgorithm alg = encryptType == EncryptTypeGcm ? CryptoAlgorithm.AesGcm : CryptoAlgorithm.AesCbc;
            if (encryptType != EncryptTypeCbc && encryptType != EncryptTypeGcm)
                throw new NotSupportedException("解密类型未知，跳过文件");

            var iterations = br.ReadInt32();
            var saltLen = br.ReadInt32();
            if (saltLen < 0 || saltLen > 256) throw new InvalidDataException("文件头损坏");
            var salt = br.ReadBytes(saltLen);
            int aesKeySizeBits = br.ReadInt32();
            if (aesKeySizeBits == 0) aesKeySizeBits = 256;
            string? originalFileName = null;
            var nameLen = br.ReadInt32();
            if (nameLen > 0 && nameLen < 4096)
                originalFileName = System.Text.Encoding.UTF8.GetString(br.ReadBytes(nameLen));

            // 优化输出FileStream配置
            var outFileOptions = FileOptions.SequentialScan | FileOptions.WriteThrough;
            using var outFs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, outFileOptions);

            switch (alg)
            {
                case CryptoAlgorithm.AesCbc:
                    await DecryptAesCbcAsync(inFs, outFs, password, salt, iterations, aesKeySizeBits, progress, ct);
                    break;
                case CryptoAlgorithm.AesGcm:
#if NET48
                    throw new NotSupportedException(RuntimeHelper.GetAesGcmRequirementMessage());
#else
                    await DecryptAesGcmAsync(inFs, outFs, password, salt, iterations, aesKeySizeBits, progress, ct);
                    break;
#endif
                case CryptoAlgorithm.TripleDes:
                    await DecryptTripleDesAsync(inFs, outFs, password, salt, iterations, progress, ct);
                    break;
                case CryptoAlgorithm.Xor:
                    await DecryptXorAsync(inFs, outFs, password, salt, iterations, progress, ct);
                    break;
                default:
                    throw new NotSupportedException("不支持的算法");
            }
            return new DecryptResult { OriginalFileName = originalFileName };
        }

        private static byte[] RandomBytes(int len)
        {
            var data = new byte[len];
            Compat.RngFill(data);
            return data;
        }

        private static byte[] DeriveKey(string password, byte[] salt, int iterations, int keySize)
        {
            var cache = KeyCache.Value;
            if (cache != null && cache.Password == password &&
                cache.Salt != null && ArraysEqual(cache.Salt, salt) &&
                cache.Iterations == iterations &&
                cache.KeySize == keySize &&
                cache.Key != null)
            {
                return cache.Key;
            }

            // 计算新密钥（.NET 4.7.2+ 支持 HashAlgorithmName；更早版本用兼容方式）
            byte[] key;
#if NET48
            try
            {
                using (var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
                    key = kdf.GetBytes(keySize);
            }
            catch (MissingMethodException)
            {
                using (var kdf = new Rfc2898DeriveBytes(password, salt, iterations))
                    key = kdf.GetBytes(keySize);
            }
#else
            using (var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
                key = kdf.GetBytes(keySize);
#endif

            KeyCache.Value = new KeyCacheEntry
            {
                Password = password,
                Salt = salt,
                Iterations = iterations,
                KeySize = keySize,
                Key = key
            };
            return key;
        }

        private static bool ArraysEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private async Task EncryptAesCbcAsync(string inputPath, FileStream outFs, string password, byte[] salt, int iterations, int keySizeBits, IProgress<long>? progress, CancellationToken ct)
        {
            var iv = RandomBytes(16);
            using var bw = new BinaryWriter(outFs, System.Text.Encoding.UTF8, leaveOpen: true);
            bw.Write(iv.Length);
            bw.Write(iv);

            var key = DeriveKey(password, salt, iterations, keySizeBits / 8);
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using var crypto = new CryptoStream(outFs, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true);
                await CopyWithProgressAsync(inputPath, crypto, progress, ct);
                await crypto.FlushAsync(ct);
            }
        }

        private async Task DecryptAesCbcAsync(FileStream inFs, FileStream outFs, string password, byte[] salt, int iterations, int keySizeBits, IProgress<long>? progress, CancellationToken ct)
        {
            using var br = new BinaryReader(inFs, System.Text.Encoding.UTF8, leaveOpen: true);
            var ivLen = br.ReadInt32();
            var iv = br.ReadBytes(ivLen);
            var key = DeriveKey(password, salt, iterations, keySizeBits / 8);
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using var crypto = new CryptoStream(inFs, aes.CreateDecryptor(), CryptoStreamMode.Read, leaveOpen: true);
                await crypto.CopyToAsync(outFs, BufferSize, ct);
            }
        }

        private async Task EncryptTripleDesAsync(string inputPath, FileStream outFs, string password, byte[] salt, int iterations, IProgress<long>? progress, CancellationToken ct)
        {
            var iv = RandomBytes(8);
            using var bw = new BinaryWriter(outFs, System.Text.Encoding.UTF8, leaveOpen: true);
            bw.Write(iv.Length);
            bw.Write(iv);

            var key = DeriveKey(password, salt, iterations, 24);
            using (var tdes = TripleDES.Create())
            {
                tdes.Key = key;
                tdes.IV = iv;
                tdes.Mode = CipherMode.CBC;
                tdes.Padding = PaddingMode.PKCS7;
                using var crypto = new CryptoStream(outFs, tdes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true);
                await CopyWithProgressAsync(inputPath, crypto, progress, ct);
                await crypto.FlushAsync(ct);
            }
        }

        private async Task DecryptTripleDesAsync(FileStream inFs, FileStream outFs, string password, byte[] salt, int iterations, IProgress<long>? progress, CancellationToken ct)
        {
            using var br = new BinaryReader(inFs, System.Text.Encoding.UTF8, leaveOpen: true);
            var ivLen = br.ReadInt32();
            var iv = br.ReadBytes(ivLen);
            var key = DeriveKey(password, salt, iterations, 24);
            using (var tdes = TripleDES.Create())
            {
                tdes.Key = key;
                tdes.IV = iv;
                tdes.Mode = CipherMode.CBC;
                tdes.Padding = PaddingMode.PKCS7;
                using var crypto = new CryptoStream(inFs, tdes.CreateDecryptor(), CryptoStreamMode.Read, leaveOpen: true);
                await crypto.CopyToAsync(outFs, BufferSize, ct);
            }
        }

#if !NET48
        private async Task EncryptAesGcmAsync(string inputPath, FileStream outFs, string password, byte[] salt, int iterations, int keySizeBits, IProgress<long>? progress, CancellationToken ct)
        {
            var key = DeriveKey(password, salt, iterations, keySizeBits / 8);
            var nonce = RandomBytes(12); // AES-GCM标准nonce长度
            
            await outFs.WriteAsync(nonce, 0, nonce.Length, ct);
            
            using var aesGcm = new AesGcm(key, 16); // 明确指定16字节的认证标签大小
            using var inputFs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
            
            var fileSize = inputFs.Length;
            var totalBytesRead = 0L;
            var pool = GetBufferPool();
            var buffer = pool.Rent(BufferSize);
            var cipherBuffer = pool.Rent(BufferSize + 16); // +16 for authentication tag
            
            try
            {
                int bytesRead;
                while ((bytesRead = await inputFs.ReadAsync(buffer, 0, BufferSize, ct)) > 0)
                {
                    var tag = new byte[16];
                    var plaintext = new byte[bytesRead];
                    var ciphertext = new byte[bytesRead];
                    
                    Array.Copy(buffer, 0, plaintext, 0, bytesRead);
                    aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
                    
                    await outFs.WriteAsync(ciphertext, 0, bytesRead, ct);
                    await outFs.WriteAsync(tag, 0, tag.Length, ct);
                    
                    totalBytesRead += bytesRead;
                    progress?.Report(totalBytesRead);
                }
            }
            finally
            {
                pool.Return(buffer);
                pool.Return(cipherBuffer);
            }
        }

        private async Task DecryptAesGcmAsync(FileStream inFs, FileStream outFs, string password, byte[] salt, int iterations, int keySizeBits, IProgress<long>? progress, CancellationToken ct)
        {
            var key = DeriveKey(password, salt, iterations, keySizeBits / 8);
            var nonce = new byte[12];
            await inFs.ReadAsync(nonce, 0, nonce.Length, ct);
            
            using var aesGcm = new AesGcm(key, 16); // 明确指定16字节的认证标签大小
            
            var fileSize = inFs.Length - nonce.Length;
            var totalBytesRead = 0L;
            var pool = GetBufferPool();
            var buffer = pool.Rent(BufferSize + 16); // +16 for authentication tag
            var plainBuffer = pool.Rent(BufferSize);
            
            try
            {
                int bytesRead;
                while ((bytesRead = await inFs.ReadAsync(buffer, 0, BufferSize + 16, ct)) > 0)
                {
                    if (bytesRead < 16) break; // 需要至少16字节的认证标签
                    
                    var cipherLength = bytesRead - 16;
                    var tag = new byte[16];
                    var ciphertext = new byte[cipherLength];
                    var plaintext = new byte[cipherLength];
                    
                    Array.Copy(buffer, cipherLength, tag, 0, 16);
                    Array.Copy(buffer, 0, ciphertext, 0, cipherLength);
                    
                    aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
                    
                    await outFs.WriteAsync(plaintext, 0, cipherLength, ct);
                    
                    totalBytesRead += cipherLength;
                    progress?.Report(totalBytesRead);
                }
            }
            finally
            {
                pool.Return(buffer);
                pool.Return(plainBuffer);
            }
        }
#endif



        // 并行处理大文件的优化方法
        private async Task CopyWithProgressParallelAsync(string inputPath, Stream outStream, IProgress<long>? progress, CancellationToken ct)
        {
            var fileInfo = new FileInfo(inputPath);
            var fileSize = fileInfo.Length;
            
            // 对于小文件（<50MB），使用单线程处理
            if (fileSize < 50 * 1024 * 1024)
            {
                await CopyWithProgressAsync(inputPath, outStream, progress, ct);
                return;
            }

            // 对于大文件，使用并行处理
            var fileOptions = FileOptions.SequentialScan;
            using var inFs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, fileOptions);
            
            // 计算并行度（基于CPU核心数）
            var parallelism = Math.Min(Environment.ProcessorCount, 4); // 最多4个并行任务
            var chunkSize = BufferSize * 2; // 每个块的大小
            var totalBytesProcessed = 0L;
            
            var semaphore = new SemaphoreSlim(parallelism, parallelism);
            var tasks = new List<Task>();
            
            while (totalBytesProcessed < fileSize)
            {
                await semaphore.WaitAsync(ct);
                
                var currentChunkSize = (int)Math.Min(chunkSize, fileSize - totalBytesProcessed);
                var chunkPool = GetBufferPool();
                var buffer = chunkPool.Rent(currentChunkSize);
                
                var task = Task.Run(async () =>
                {
                    try
                    {
                        var bytesRead = await inFs.ReadAsync(buffer, 0, currentChunkSize, ct);
                        if (bytesRead > 0)
                        {
                            await outStream.WriteAsync(buffer, 0, bytesRead, ct);
                            progress?.Report(bytesRead);
                        }
                    }
                    finally
                    {
                        chunkPool.Return(buffer);
                        semaphore.Release();
                    }
                }, ct);
                
                tasks.Add(task);
                totalBytesProcessed += currentChunkSize;
                
                // 限制并发任务数量
                if (tasks.Count >= parallelism * 2)
                {
                    await Task.WhenAny(tasks);
                    tasks.RemoveAll(t => t.IsCompleted);
                }
            }
            
            await Task.WhenAll(tasks);
        }

        // 优化的CopyWithProgressAsync方法，自动选择并行或单线程
        private async Task CopyWithProgressAsync(string inputPath, Stream outStream, IProgress<long>? progress, CancellationToken ct)
        {
            // 优化FileStream配置：使用顺序访问提示和更大的缓冲区
            var fileOptions = FileOptions.SequentialScan;
            FileStream? inFs = null;
            string? tempCopy = null;
            try
            {
                inFs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, fileOptions);
            }
            catch (IOException)
            {
                // 可能被占用：尝试复制到临时文件再读取（如果复制也失败则继续抛出）
                tempCopy = TryCreateTempCopy(inputPath);
                if (tempCopy != null)
                {
                    inFs = new FileStream(tempCopy, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, fileOptions);
                }
                else
                {
                    throw;
                }
            }
            catch (UnauthorizedAccessException)
            {
                tempCopy = TryCreateTempCopy(inputPath);
                if (tempCopy != null)
                {
                    inFs = new FileStream(tempCopy, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, fileOptions);
                }
                else
                {
                    throw;
                }
            }
            
            var pool = GetBufferPool();
            var buffer = pool.Rent(BufferSize);
            try
            {
                int bytesRead;
                while ((bytesRead = await inFs.ReadAsync(buffer, 0, BufferSize, ct)) > 0)
                {
                    await outStream.WriteAsync(buffer, 0, bytesRead, ct);
                    progress?.Report(bytesRead);
                }
            }
            finally
            {
                pool.Return(buffer);
                try { inFs?.Dispose(); } catch { }
                if (!string.IsNullOrEmpty(tempCopy))
                {
                    try { File.Delete(tempCopy); } catch { }
                }
            }
        }

        private static string? TryCreateTempCopy(string inputPath)
        {
            try
            {
                var src = new FileInfo(inputPath);
                if (!src.Exists) return null;
                string tempDir = Path.Combine(Path.GetTempPath(), "encryptTools_temp");
                Directory.CreateDirectory(tempDir);
                string tempFile = Path.Combine(tempDir, "src_" + Guid.NewGuid().ToString("N") + src.Extension);
                if (File.Exists(tempFile)) File.Delete(tempFile);
                File.Copy(inputPath, tempFile);
                return tempFile;
            }
            catch
            {
                return null;
            }
        }

        // 优化XOR加密方法，使用ArrayPool
        private async Task EncryptXorAsync(string inputPath, FileStream outFs, string password, byte[] salt, int iterations, IProgress<long>? progress, CancellationToken ct)
        {
            // 简单演示用，不安全的算法
            var key = DeriveKey(password, salt, iterations, 32);
            
            // 优化FileStream配置
            var fileOptions = FileOptions.SequentialScan;
            using var inFs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, fileOptions);
            
            var pool = GetBufferPool();
            var buffer = pool.Rent(BufferSize);
            try
            {
                long total = 0;
                int keyIndex = 0;
                int bytesRead;
                while ((bytesRead = await inFs.ReadAsync(buffer, 0, BufferSize, ct)) > 0)
                {
                    for (int i = 0; i < bytesRead; i++)
                    {
                        buffer[i] ^= key[keyIndex];
                        keyIndex = (keyIndex + 1) % key.Length;
                    }
                    await outFs.WriteAsync(buffer, 0, bytesRead, ct);
                    total += bytesRead;
                    progress?.Report(bytesRead);
                }
            }
            finally
            {
                pool.Return(buffer);
            }
        }

        // 优化XOR解密方法
        private async Task DecryptXorAsync(FileStream inFs, FileStream outFs, string password, byte[] salt, int iterations, IProgress<long>? progress, CancellationToken ct)
        {
            var key = DeriveKey(password, salt, iterations, 32);
            var pool = GetBufferPool();
            var buffer = pool.Rent(BufferSize);
            try
            {
                int keyIndex = 0;
                int bytesRead;
                while ((bytesRead = await inFs.ReadAsync(buffer, 0, BufferSize, ct)) > 0)
                {
                    for (int i = 0; i < bytesRead; i++)
                    {
                        buffer[i] ^= key[keyIndex];
                        keyIndex = (keyIndex + 1) % key.Length;
                    }
                    await outFs.WriteAsync(buffer, 0, bytesRead, ct);
                    progress?.Report(bytesRead);
                }
            }
            finally
            {
                pool.Return(buffer);
            }
        }
    }
}