Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "STARTING SMART ENVIRONMENT SETUP FOR DATA ARCHITECT PLATFORM" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# --- FUNCTION TO VERIFY IF APPLICATION IS INSTALLED ---
function Test-AppInstalled ($appName, $cliCommand) {
    if ($cliCommand -and (Get-Command $cliCommand -ErrorAction SilentlyContinue)) { return $true }
    
    $regPaths = @(
        "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )
    foreach ($path in $regPaths) {
        if (Get-ItemProperty $path -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like "*$appName*" -or $_.PsChildName -like "*$appName*" }) {
            return $true
        }
    }
    return $false
}

# --- CHOCOLATEY PACKAGE MANAGER INITIALIZATION ---
if (Get-Command choco -ErrorAction SilentlyContinue) {
    Write-Host "[OK] Chocolatey is already installed on this machine." -ForegroundColor Green
} else {
    Write-Host "Chocolatey not found. Initiating clean installation..." -ForegroundColor Yellow
    Set-ExecutionPolicy Bypass -Scope Process -Force
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072
    $installScript = (New-Object System.Net.WebClient).DownloadString('https://chocolatey.org')
    iex $installScript
}

# --- STEP 1: HOST RUNTIMES VERIFICATION & INSTALLATION ---
Write-Host "Step 1: Verifying and deploying host-level dependencies..." -ForegroundColor Cyan

if (Test-AppInstalled "Docker" "docker") {
    Write-Host "[SKIP] Docker Desktop detected. Preserving existing environment." -ForegroundColor Green
} else {
    Write-Host "[INSTALL] Docker Desktop not found. Deploying via Chocolatey..." -ForegroundColor Yellow
    choco install docker-desktop -y --no-progress
}

if (Test-AppInstalled ".NET SDK" "dotnet") {
    Write-Host "[SKIP] .NET SDK detected. Preserving existing environment." -ForegroundColor Green
} else {
    Write-Host "[INSTALL] .NET SDK not found. Deploying via Chocolatey..." -ForegroundColor Yellow
    choco install dotnet-sdk -y --no-progress
}

if (Test-AppInstalled "Node.js" "node") {
    Write-Host "[SKIP] Node.js detected. Preserving existing environment." -ForegroundColor Green
} else {
    Write-Host "[INSTALL] Node.js LTS not found. Deploying via Chocolatey..." -ForegroundColor Yellow
    choco install nodejs-lts -y --no-progress
}

if (Test-AppInstalled "Cursor" "cursor") {
    Write-Host "[SKIP] Cursor IDE detected. Preserving existing environment." -ForegroundColor Green
} else {
    Write-Host "[INSTALL] Cursor IDE not found. Deploying via Chocolatey..." -ForegroundColor Yellow
    choco install cursoride -y --no-progress
}

# --- STEP 2: ENVIRONMENT PATH REFRESH ---
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

# --- STEP 3: IDE EXTENSIONS PROVISIONING ---
Write-Host "Step 2: Injecting required extension profiles into Cursor..." -ForegroundColor Cyan
if (Get-Command cursor -ErrorAction SilentlyContinue) {
    Write-Host "Installing workspace extensions..." -ForegroundColor Yellow
    cursor --install-extension ms-dotnettools.csdevkit
    cursor --install-extension ms-dotnettools.aspire
    cursor --install-extension ms-python.python
    Write-Host "[OK] Extensions deployed successfully." -ForegroundColor Green
} else {
    Write-Host "[WARNING] The 'cursor' CLI command is not yet active in this session." -ForegroundColor Red
    Write-Host "Please launch Cursor manually after rebooting and install: C# Dev Kit, .NET Aspire, and Python." -ForegroundColor White
}

# --- STEP 4: .NET ASPIRE WORKLOAD PROVISIONING ---
Write-Host "Step 3: Verifying .NET Aspire target ecosystem workloads..." -ForegroundColor Cyan
$aspireCheck = dotnet workload list | Where-Object { $_ -like "*aspire*" }
if ($aspireCheck) {
    Write-Host "[SKIP] .NET Aspire orchestration workloads are already active." -ForegroundColor Green
} else {
    Write-Host "[INSTALL] Installing .NET Aspire workload targets..." -ForegroundColor Yellow
    dotnet workload install aspire
}

Write-Host "==========================================================" -ForegroundColor Green
Write-Host "SMART INITIALIZATION PROCESS COMPLETED SUCCESSFULLY" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "Status: Missing applications deployed, pre-existing runtimes untouched." -ForegroundColor White
Write-Host "Actions: Restart your PC, open Cursor, and trigger Composer via Ctrl+I" -ForegroundColor Yellow
