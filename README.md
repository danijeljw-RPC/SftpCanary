# SftpCanary

SftpCanary is a small .NET console utility that:
- Tests TCP connectivity to an SFTP endpoint (host + port)
- Optionally performs an SFTP login/logout using username and password
- Can resolve DNS A/AAAA records for the host
- Returns meaningful exit codes for CI/CD / scripted checks

It is designed to be used both interactively and inside automation (e.g. GitHub Actions, scheduled jobs, or monitoring scripts).

---

## Features

- IPv4/IPv6 DNS resolution (`--resolve-dns`)
- TCP connectivity test (similar to `Test-NetConnection` for a specific host/port)
- Optional SFTP login/logout test using user/password
- Simple, CLI-friendly argument model
- Clear exit codes for scripting/automation

---

## Command-line Usage

```text
SftpCanary --uri=<host> [--port=<port>] [--user=<user>] [--pass=<pass>] [--resolve-dns]
```

### Arguments

- `--uri=<host>`  
  Required. Target SFTP host or URI.  
  Examples:  
  - `--uri=test.rebex.net`  
  - `--uri=sftp.example.com`  
  If a full URI such as `sftp://test.rebex.net/` is supplied, the host is extracted automatically.

- `--port=<port>`  
  Optional. TCP port to test. Defaults to `22` if omitted.

- `--user=<user>`  
  Optional. SFTP username. If **both** `--user` and `--pass` are provided, SftpCanary attempts a full SFTP login/logout and directory listing of `/`.

- `--pass=<pass>`  
  Optional. SFTP password. Used only when `--user` is provided as well.

- `--resolve-dns`  
  Optional flag. If present, SftpCanary resolves DNS A (IPv4) and AAAA (IPv6) records for the host and prints them before running connectivity tests.

- `--help`, `-h`, `/?`  
  Prints usage and exits.

---

## Behaviour

### 1. DNS Resolution (optional)

If `--resolve-dns` is supplied, SftpCanary uses `System.Net.Dns.GetHostAddresses` to resolve the host from `--uri` and prints:

- A (IPv4) records (if any)
- AAAA (IPv6) records (if any)

### 2. TCP Connectivity Test (always)

SftpCanary always attempts a TCP connection to `host:port`:

- On success, a message similar to `TCP connection to host:port succeeded.` is printed.
- On failure (timeout, DNS, or socket error), an error message is printed and SftpCanary exits with a non-zero exit code (see **Exit Codes**).

If the TCP connection fails, SftpCanary does **not** attempt SFTP login.

### 3. SFTP Login / Logout Test (optional)

If both `--user` and `--pass` are supplied, SftpCanary:

1. Opens an SFTP connection using SSH.NET (`SftpClient`).
2. Lists the contents of `/` (root directory) as a sanity check.
3. Prints file name, length, and last write time for each non-trivial entry.
4. Disconnects cleanly.

If login fails (authentication error, key mismatch, etc.), SftpCanary prints the exception message and returns a non-zero exit code.

If either `--user` or `--pass` is missing, SftpCanary **only** performs the TCP connectivity test and skips the login step.

---

## Exit Codes

The return codes are designed to be easy to consume from scripts and CI pipelines:

- `0` – Success  
  TCP connectivity succeeded, and (if login test requested) SFTP login/logout succeeded.

- `1` – Usage / argument error  
  For example, missing `--uri` or invalid port value.

- `2` – TCP connectivity failure  
  Unable to connect to `host:port` within the timeout (or other socket-level failure).

Additional non-zero codes may be returned for unexpected/unhandled exceptions if present. In normal operation, you should see only `0`, `1`, or `2`.

---

## Examples

### 1. Simple connectivity check

```powershell
SftpCanary --uri=test.rebex.net
```

- Resolves `test.rebex.net` using .NET networking
- Attempts TCP connection on port 22
- Skips SFTP login because `--user` and `--pass` were not supplied

### 2. Connectivity + login against public test SFTP

Using the Rebex public test server:

```powershell
SftpCanary --uri=test.rebex.net --user=demo --pass=password
```

Expected behaviour:

- TCP connection to `test.rebex.net:22`
- SFTP login as `demo/password`
- List directory `/`
- Logout and exit code `0` on success

### 3. DNS resolution + connectivity only

```powershell
SftpCanary --uri=sftp.example.com --resolve-dns
```

- Prints IPv4/IPv6 addresses (if resolvable)
- Tests TCP connection to `sftp.example.com:22`
- Skips login because no credentials provided

### 4. Using from PowerShell with exit-code check

```powershell
& .\SftpCanary.exe --uri=test.rebex.net --user=demo --pass=password
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    Write-Host "SFTP canary FAILED with code $exitCode"
    exit $exitCode
} else {
    Write-Host "SFTP canary succeeded."
}
```

This is suitable for scheduled checks or deployment validation scripts.

---

## Building SftpCanary

### Prerequisites

- .NET 9 SDK
- Git (if pulling from a repository)
- SSH.NET NuGet package

### Clone and build

```bash
git clone <your-repo-url> SftpCanary
cd SftpCanary

dotnet restore
dotnet build -c Release
```

### Publish a self-contained single-file binary (Windows)

```powershell
dotnet publish .\SftpCanary.csproj `
  -c Release `
  -r win-x64 `
  -o .\publish\win-x64 `
  -p:SelfContained=true `
  -p:PublishSingleFile=true
```

This will produce something like:

```text
publish\win-x64\SftpCanary.exe
```

You can then copy that single EXE to any Windows machine (no separate .NET runtime required).

### Linux/macOS publish

For a Linux self-contained binary:

```bash
dotnet publish ./SftpCanary.csproj   -c Release   -r linux-x64   -o ./publish/linux-x64   -p:SelfContained=true   -p:PublishSingleFile=true
```

For macOS (Apple Silicon):

```bash
dotnet publish ./SftpCanary.csproj   -c Release   -r osx-arm64   -o ./publish/osx-arm64   -p:SelfContained=true   -p:PublishSingleFile=true
```

Adjust `-r` as needed (`osx-x64` for Intel macOS, `linux-arm64` for ARM Linux, etc.).

---

## GitHub Actions Example

Below is an example GitHub Actions workflow that builds SftpCanary on Windows, publishes it, runs a test against `test.rebex.net`, and fails the job if the test fails:

```yaml
name: .NET

on:
  push:
    branches: [ "main" ]
  pull_request:
    branches: [ "main" ]

jobs:
  build:
    runs-on: windows-latest

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 9.0.x

      - name: Restore dependencies
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore

      - name: Publish & Test SftpCanary
        shell: pwsh
        run: |
          dotnet publish .\SftpCanary.csproj `
            -c Release `
            -r win-x64 `
            -o .\publish\win-x64 `
            -p:SelfContained=true `
            -p:PublishSingleFile=true

          .\publish\win-x64\SftpCanary.exe --uri=test.rebex.net --user=demo --pass=password

          $exitCode = $LASTEXITCODE
          Write-Host "SftpCanary exit code: $exitCode"

          if ($exitCode -ne 0) {
            Write-Host "SFTP canary FAILED with code $exitCode"
            exit $exitCode
          }
```

---

## Notes and Recommendations

- Avoid hard-coding real production credentials in scripts or CI pipelines. Use secrets/secure variables instead.
- Consider pointing SftpCanary at a non-production SFTP endpoint during deployment validation.
- For more advanced scenarios (key-based auth, specific ciphers, etc.), you can extend SftpCanary by configuring `PasswordConnectionInfo` / `ConnectionInfo` from SSH.NET with additional options.

SftpCanary is intended to be a simple, reliable “smoke test” for SFTP endpoints—fast to run, easy to wire into automations, and straightforward to interpret from its exit codes.
