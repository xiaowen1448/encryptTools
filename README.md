# EncryptTools - 高性能文件加密工具

一个基于 .NET 8 的现代化高性能 Windows 图形界面加密工具，支持多种加密算法和批量文件处理。

软件页面如下

<img width="824" height="438" alt="image" src="https://github.com/user-attachments/assets/53f1899d-b439-4a41-b317-a0f3172290ec" />

使用密码加密或者保存密码为文件，使用密码文件进行加密

<img width="1393" height="655" alt="image" src="https://github.com/user-attachments/assets/98748856-f15b-4aca-bd03-4a46465a9728" />

加密完成

<img width="816" height="441" alt="image" src="https://github.com/user-attachments/assets/4db52f85-fb07-40c1-8d0e-81702535ffaf" />

解密完成


<img width="824" height="438" alt="image" src="https://github.com/user-attachments/assets/743ee656-0257-4008-9e82-0327a9e55685" />


加密解密速度

硬盘参数为M2固态，测速参数如下：

<img width="621" height="580" alt="image" src="https://github.com/user-attachments/assets/ae91d426-4191-4428-a352-a884422800c1" />

文件大小文win10系统镜像,d大约6.5GB

<img width="1356" height="815" alt="image" src="https://github.com/user-attachments/assets/f8b75ece-52c1-4545-a313-f524549fb357" />

M2固态环境下，加密源文件，仅用13s 完成
<img width="1395" height="741" alt="image" src="https://github.com/user-attachments/assets/d98446e0-6584-4064-87d3-b696f858a3ba" />

解密仅需要9s 

<img width="1335" height="729" alt="image" src="https://github.com/user-attachments/assets/48eb416f-84c7-466d-bb95-98a3e3ac44eb" />


批量处理文件，这里测试的是296个图片，共计2.79G，处理速度如下图


<img width="1599" height="950" alt="image" src="https://github.com/user-attachments/assets/6e5e86db-8cc9-4105-b72b-9af1c31f5c25" />


<img width="1634" height="979" alt="image" src="https://github.com/user-attachments/assets/3c508941-bdee-4195-934e-7276868f6f83" />

[09:43:15] 开始加密到[09:43:35] 加密完成，296个文件，共计加密时间为20S，平均每秒处理15个文件，


解密速度

<img width="1626" height="878" alt="image" src="https://github.com/user-attachments/assets/4ef482b2-0e0d-4db3-9ba4-535373c40619" />

<img width="1635" height="919" alt="image" src="https://github.com/user-attachments/assets/434e270f-ce07-460a-8f91-ccbc4e0ca0e7" />

[09:47:56] 开始解密到[09:48:17] 解密完成，共计花费时间为21s,平均每秒处理15个文件




## 🚀 主要特性

### 加密算法支持
- **AES-CBC**: 高安全性对称加密（推荐）
- **AES-GCM**: 原生支持，提供认证加密（AEAD）
- **3DES**: 传统加密算法，向后兼容
- **XOR**: 简单加密，仅用于演示

### 核心功能
- 📁 支持文件和文件夹递归处理
- 🔐 PBKDF2 密钥派生（默认 200,000 次迭代）
- 📊 实时进度显示和日志记录
- ⚡ 高性能优化（4MB 缓冲区 + 对象池 + 密钥缓存）
- 🎯 支持原地处理或指定输出目录
- ❌ 支持任务取消
- 🔒 可空引用类型支持，提升代码安全性

## 🏗️ 项目架构

### 目录结构
```
EncryptTools/
├── Assets/                 # 资源文件
│   └── app.ico            # 应用程序图标
├── Crypto/                # 加密核心模块
│   ├── CryptoAlgorithm.cs # 算法枚举定义
│   ├── CryptoService.cs   # 加密/解密核心逻辑
│   └── FileEncryptor.cs   # 文件处理和进度管理
├── MainForm.cs            # 主界面逻辑
├── MainForm.Designer.cs   # 界面设计器代码
├── Program.cs             # 程序入口点
├── EncryptTools.csproj    # 项目配置文件
└── README.md              # 项目文档
```

### 核心组件

#### 1. CryptoService (加密服务)
- **职责**: 实现各种加密算法的核心逻辑
- **优化**: 
  - 4MB 缓冲区提升 I/O 性能
  - 线程本地密钥缓存避免重复计算
  - 异步操作支持大文件处理
  - AES/3DES 对象池减少创建开销
  - ArrayPool 缓冲区管理优化内存使用
  - 原生 AES-GCM 支持提供认证加密

#### 2. FileEncryptor (文件处理器)
- **职责**: 文件和文件夹的递归处理
- **功能**: 路径管理、进度汇总、错误处理

#### 3. MainForm (用户界面)
- **职责**: 用户交互和界面逻辑
- **特性**: 响应式设计、实时进度更新

## 🔧 系统要求

### Windows 版本兼容性
| Windows 版本 | 默认 .NET 版本 | 支持状态 | 备注 |
|-------------|---------------|---------|------|
| Windows 10 1809+ | .NET Framework 4.7.2+ | ✅ 支持 | 需要 .NET 8 Runtime |
| Windows 11 | .NET Framework 4.8+ | ✅ 支持 | 完全兼容 |
| Windows Server 2019+ | .NET Framework 4.7.2+ | ✅ 支持 | 服务器环境兼容 |

### 最低要求
- Windows 10 版本 1809 或更高版本
- .NET 8 Runtime (x64)
- 至少 100MB 可用磁盘空间
- 推荐 4GB+ 内存用于大文件处理

## 🛠️ 构建和部署

### 开发环境构建

#### 框架依赖版本 (推荐用于开发)
```bash
# 还原依赖
dotnet restore EncryptTools.FrameworkDependent.csproj

# 调试构建
dotnet build EncryptTools.FrameworkDependent.csproj

# 发布构建
dotnet build EncryptTools.FrameworkDependent.csproj -c Release

# 运行程序
dotnet run --project EncryptTools.FrameworkDependent.csproj
```

#### 自包含版本 (用于发布)
```bash
# 还原依赖
dotnet restore EncryptTools.SelfContained.csproj

# 调试构建
dotnet build EncryptTools.SelfContained.csproj

# 发布构建
dotnet build EncryptTools.SelfContained.csproj -c Release
```


### 发布为独立可执行文件

#### 使用构建脚本 (推荐)
```bash
# 框架依赖版本 (体积小，需要.NET Runtime)
build-framework-dependent.bat

# 自包含版本 (体积大，无需.NET Runtime)
build-self-contained.bat
```

#### 手动构建命令
```bash
# 框架依赖版本 - Windows x64
dotnet publish EncryptTools.FrameworkDependent.csproj -c Release -r win-x64 --self-contained false

# 自包含版本 - Windows x64 (推荐)
dotnet publish EncryptTools.SelfContained.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true

# 自包含版本 - Windows x86 (32位兼容)
dotnet publish EncryptTools.SelfContained.csproj -c Release -r win-x86 --self-contained true /p:PublishSingleFile=true

# 自包含版本 - Windows ARM64 (新架构支持)
dotnet publish EncryptTools.SelfContained.csproj -c Release -r win-arm64 --self-contained true /p:PublishSingleFile=true
```

**输出位置:**
- 框架依赖版本: `publish-framework-dependent/EncryptTools.FrameworkDependent.exe` (~0.3MB)
- 自包含版本: `publish-single-file-win-x64/EncryptTools.SelfContained.exe` (~68MB)



