using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace EncryptTools
{
    public class CryptoService
    {
        private const string Magic = "ENC1";
        private const int BufferSize = 4 * 1024 * 1024; // 4MB缓冲区
        
        // 缓存密钥派生结果，避免重复计算
        private static readonly ThreadLocal<(string password, byte[] salt, int iterations, int keySize, byte[] key)> KeyCache = 
            new ThreadLocal<(string, byte[], int, int, byte[])>();

        // 缓冲区池，减少内存分配
        private static readonly ArrayPool<byte> BufferPool = ArrayPool<byte>.Shared;

        // 预分配的加密对象池，减少创建开销
        private static readonly ConcurrentQueue<Aes> AesPool = new ConcurrentQueue<Aes>();
        private static readonly ConcurrentQueue<TripleDES> TripleDesPool = new ConcurrentQueue<TripleDES>();

        // 获取AES实例
        private static Aes GetAes()
        {
            if (AesPool.TryDequeue(out var aes))
            {
                return aes;
            }
            return Aes.Create();
        }

        // 归还AES实例
        private static void ReturnAes(Aes aes)
        {
            if (aes != null && AesPool.Count < 10) // 限制池大小
            {
                aes.Clear();
                AesPool.Enqueue(aes);
            }
            else
            {
                aes?.Dispose();
            }
        }

        // 获取TripleDES实例
        private static TripleDES GetTripleDes()
        {
            if (TripleDesPool.TryDequeue(out var tdes))
            {
                return tdes;
            }
            return TripleDES.Create();
        }

        // 归还TripleDES实例
        private static void ReturnTripleDes(TripleDES tdes)
        {
            if (tdes != null && TripleDesPool.Count < 10) // 限制池大小
            {
                tdes.Clear();
                TripleDesPool.Enqueue(tdes);
            }
            else
            {
                tdes?.Dispose();
            }
        }

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
            var salt = RandomBytes(16);
            
            // 优化FileStream配置：使用更大的缓冲区和顺序访问提示
            var fileOptions = FileOptions.SequentialScan | FileOptions.WriteThrough;
            using var outFs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, fileOptions);
            using var bw = new BinaryWriter(outFs);

            // Header (ENC3) - 增加原始文件名元数据
            bw.Write(System.Text.Encoding.ASCII.GetBytes("ENC3"));
            bw.Write((byte)algorithm);
            bw.Write(iterations);
            bw.Write(salt.Length);
            bw.Write(salt);
            // Write key size for AES algorithms; others write 0
            var keySizeToWrite = (algorithm == CryptoAlgorithm.AesCbc || algorithm == CryptoAlgorithm.AesGcm) ? aesKeySizeBits : 0;
            bw.Write(keySizeToWrite);

            // 写入原始文件名（仅文件名，不含路径），UTF-8编码
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
                    await EncryptAesGcmAsync(inputPath, outFs, password, salt, iterations, aesKeySizeBits, progress, ct);
                    break;
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

        public async Task<DecryptResult> DecryptFileAsync(
            string inputPath,
            string outputPath,
            string password,
            IProgress<long>? progress,
            CancellationToken ct)
        {
            // 优化FileStream配置：使用顺序访问提示
            var fileOptions = FileOptions.SequentialScan;
            using var inFs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, fileOptions);
            using var br = new BinaryReader(inFs);
            var magic = new byte[4];
            var readMagic = inFs.Read(magic, 0, magic.Length);
            if (readMagic != 4 || magic[0] != (byte)'E' || magic[1] != (byte)'N' || magic[2] != (byte)'C')
                throw new InvalidDataException("文件不是受支持的加密格式");
            var headerVersion = magic[3];

            var alg = (CryptoAlgorithm)br.ReadByte();
            var iterations = br.ReadInt32();
            var saltLen = br.ReadInt32();
            var salt = br.ReadBytes(saltLen);
            int aesKeySizeBits = 256; // default for ENC1
            string? originalFileName = null;
            if (headerVersion == (byte)'2')
            {
                aesKeySizeBits = br.ReadInt32();
                if (aesKeySizeBits == 0 && (alg == CryptoAlgorithm.AesCbc || alg == CryptoAlgorithm.AesGcm))
                {
                    aesKeySizeBits = 256; // fallback safety
                }
            }
            else if (headerVersion == (byte)'3')
            {
                aesKeySizeBits = br.ReadInt32();
                if (aesKeySizeBits == 0 && (alg == CryptoAlgorithm.AesCbc || alg == CryptoAlgorithm.AesGcm))
                {
                    aesKeySizeBits = 256; // fallback safety
                }
                // 读取原始文件名
                var nameLen = br.ReadInt32();
                if (nameLen > 0 && nameLen < 1024 * 4) // 简单防御
                {
                    var nameBytes = br.ReadBytes(nameLen);
                    originalFileName = System.Text.Encoding.UTF8.GetString(nameBytes);
                }
            }

            // 优化输出FileStream配置
            var outFileOptions = FileOptions.SequentialScan | FileOptions.WriteThrough;
            using var outFs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, outFileOptions);

            switch (alg)
            {
                case CryptoAlgorithm.AesCbc:
                    await DecryptAesCbcAsync(inFs, outFs, password, salt, iterations, aesKeySizeBits, progress, ct);
                    break;
                case CryptoAlgorithm.AesGcm:
                    await DecryptAesGcmAsync(inFs, outFs, password, salt, iterations, aesKeySizeBits, progress, ct);
                    break;
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
            RandomNumberGenerator.Fill(data);
            return data;
        }

        private static byte[] DeriveKey(string password, byte[] salt, int iterations, int keySize)
        {
            // 检查缓存
            var cache = KeyCache.Value;
            if (cache.password == password && 
                ArraysEqual(cache.salt, salt) && 
                cache.iterations == iterations && 
                cache.keySize == keySize)
            {
                return cache.key;
            }

            // 计算新密钥
            using var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
            var key = kdf.GetBytes(keySize);
            
            // 更新缓存
            KeyCache.Value = (password, salt, iterations, keySize, key);
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
            var aes = GetAes(); // 使用对象池
            try
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var crypto = new CryptoStream(outFs, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true);
                await CopyWithProgressAsync(inputPath, crypto, progress, ct);
                await crypto.FlushAsync(ct);
            }
            finally
            {
                ReturnAes(aes); // 归还到对象池
            }
        }

        private async Task DecryptAesCbcAsync(FileStream inFs, FileStream outFs, string password, byte[] salt, int iterations, int keySizeBits, IProgress<long>? progress, CancellationToken ct)
        {
            using var br = new BinaryReader(inFs, System.Text.Encoding.UTF8, leaveOpen: true);
            var ivLen = br.ReadInt32();
            var iv = br.ReadBytes(ivLen);
            var key = DeriveKey(password, salt, iterations, keySizeBits / 8);
            var aes = GetAes(); // 使用对象池
            try
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using var crypto = new CryptoStream(inFs, aes.CreateDecryptor(), CryptoStreamMode.Read, leaveOpen: true);
                await crypto.CopyToAsync(outFs, BufferSize, ct);
            }
            finally
            {
                ReturnAes(aes); // 归还到对象池
            }
        }

        private async Task EncryptTripleDesAsync(string inputPath, FileStream outFs, string password, byte[] salt, int iterations, IProgress<long>? progress, CancellationToken ct)
        {
            var iv = RandomBytes(8);
            using var bw = new BinaryWriter(outFs, System.Text.Encoding.UTF8, leaveOpen: true);
            bw.Write(iv.Length);
            bw.Write(iv);

            var key = DeriveKey(password, salt, iterations, 24);
            var tdes = GetTripleDes(); // 使用对象池
            try
            {
                tdes.Key = key;
                tdes.IV = iv;
                tdes.Mode = CipherMode.CBC;
                tdes.Padding = PaddingMode.PKCS7;
                using var crypto = new CryptoStream(outFs, tdes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true);
                await CopyWithProgressAsync(inputPath, crypto, progress, ct);
                await crypto.FlushAsync(ct);
            }
            finally
            {
                ReturnTripleDes(tdes); // 归还到对象池
            }
        }

        private async Task DecryptTripleDesAsync(FileStream inFs, FileStream outFs, string password, byte[] salt, int iterations, IProgress<long>? progress, CancellationToken ct)
        {
            using var br = new BinaryReader(inFs, System.Text.Encoding.UTF8, leaveOpen: true);
            var ivLen = br.ReadInt32();
            var iv = br.ReadBytes(ivLen);
            var key = DeriveKey(password, salt, iterations, 24);
            var tdes = GetTripleDes(); // 使用对象池
            try
            {
                tdes.Key = key;
                tdes.IV = iv;
                tdes.Mode = CipherMode.CBC;
                tdes.Padding = PaddingMode.PKCS7;
                using var crypto = new CryptoStream(inFs, tdes.CreateDecryptor(), CryptoStreamMode.Read, leaveOpen: true);
                await crypto.CopyToAsync(outFs, BufferSize, ct);
            }
            finally
            {
                ReturnTripleDes(tdes); // 归还到对象池
            }
        }

        private async Task EncryptAesGcmAsync(string inputPath, FileStream outFs, string password, byte[] salt, int iterations, int keySizeBits, IProgress<long>? progress, CancellationToken ct)
        {
            var key = DeriveKey(password, salt, iterations, keySizeBits / 8);
            var nonce = RandomBytes(12); // AES-GCM标准nonce长度
            
            await outFs.WriteAsync(nonce, 0, nonce.Length, ct);
            
            using var aesGcm = new AesGcm(key, 16); // 明确指定16字节的认证标签大小
            using var inputFs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
            
            var fileSize = inputFs.Length;
            var totalBytesRead = 0L;
            var buffer = BufferPool.Rent(BufferSize);
            var cipherBuffer = BufferPool.Rent(BufferSize + 16); // +16 for authentication tag
            
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
                BufferPool.Return(buffer);
                BufferPool.Return(cipherBuffer);
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
            var buffer = BufferPool.Rent(BufferSize + 16); // +16 for authentication tag
            var plainBuffer = BufferPool.Rent(BufferSize);
            
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
                BufferPool.Return(buffer);
                BufferPool.Return(plainBuffer);
            }
        }



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
                var buffer = BufferPool.Rent(currentChunkSize);
                
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
                        BufferPool.Return(buffer);
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
            using var inFs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, fileOptions);
            
            // 使用ArrayPool减少内存分配
            var buffer = BufferPool.Rent(BufferSize);
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
                BufferPool.Return(buffer);
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
            
            // 使用ArrayPool减少内存分配
            var buffer = BufferPool.Rent(BufferSize);
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
                BufferPool.Return(buffer);
            }
        }

        // 优化XOR解密方法，使用ArrayPool
        private async Task DecryptXorAsync(FileStream inFs, FileStream outFs, string password, byte[] salt, int iterations, IProgress<long>? progress, CancellationToken ct)
        {
            var key = DeriveKey(password, salt, iterations, 32);
            
            // 使用ArrayPool减少内存分配
            var buffer = BufferPool.Rent(BufferSize);
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
                BufferPool.Return(buffer);
            }
        }
    }
}