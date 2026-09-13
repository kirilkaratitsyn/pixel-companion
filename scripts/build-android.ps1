param(
  [Parameter(Mandatory=$true)][string]$Jdk,
  [Parameter(Mandatory=$true)][string]$BuildTools,
  [Parameter(Mandatory=$true)][string]$AndroidJar,
  [Parameter(Mandatory=$true)][string]$WorkDirectory,
  [Parameter(Mandatory=$true)][string]$Output,
  [Parameter(Mandatory=$true)][string]$Keystore
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$android=Join-Path $repo 'apps/android'
New-Item -ItemType Directory -Force -Path $WorkDirectory,(Join-Path $WorkDirectory 'classes'),(Join-Path $WorkDirectory 'dex'),(Split-Path $Output -Parent) | Out-Null
$java=Join-Path $Jdk 'bin/java.exe'
$javac=Join-Path $Jdk 'bin/javac.exe'
$env:JAVA_HOME=$Jdk
function Check($name) { if($LASTEXITCODE -ne 0){throw "$name failed with exit $LASTEXITCODE"} }
$sources=@(Get-ChildItem -LiteralPath (Join-Path $android 'src') -Filter '*.java' -Recurse | ForEach-Object {$_.FullName})
& $javac -encoding UTF-8 -source 8 -target 8 -classpath $AndroidJar -d (Join-Path $WorkDirectory 'classes') @sources
Check 'javac'
& (Join-Path $Jdk 'bin/jar.exe') cf (Join-Path $WorkDirectory 'classes.jar') -C (Join-Path $WorkDirectory 'classes') .
Check 'jar'
& $java -cp (Join-Path $BuildTools 'lib/d8.jar') com.android.tools.r8.D8 --lib $AndroidJar --min-api 28 --output (Join-Path $WorkDirectory 'dex') (Join-Path $WorkDirectory 'classes.jar')
Check 'd8'
$unsigned=Join-Path $WorkDirectory 'unsigned.apk'
& (Join-Path $BuildTools 'aapt2.exe') link -I $AndroidJar --manifest (Join-Path $android 'AndroidManifest.xml') -o $unsigned
Check 'aapt2'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[System.IO.Compression.ZipFile]::Open($unsigned,[System.IO.Compression.ZipArchiveMode]::Update)
try { [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $WorkDirectory 'dex/classes.dex'),'classes.dex') | Out-Null } finally {$archive.Dispose()}
$aligned=Join-Path $WorkDirectory 'aligned.apk'
& (Join-Path $BuildTools 'zipalign.exe') -f 4 $unsigned $aligned
Check 'zipalign'
if(-not(Test-Path -LiteralPath $Keystore)) {
  New-Item -ItemType Directory -Force -Path (Split-Path $Keystore -Parent) | Out-Null
  & (Join-Path $Jdk 'bin/keytool.exe') -genkeypair -keystore $Keystore -storepass android -keypass android -alias pixel-prototype -dname 'CN=Pixel Companion Prototype' -keyalg RSA -keysize 2048 -validity 3650 -noprompt
  Check 'keytool'
}
& $java -jar (Join-Path $BuildTools 'lib/apksigner.jar') sign --ks $Keystore --ks-key-alias pixel-prototype --ks-pass pass:android --key-pass pass:android --out $Output $aligned
Check 'apksigner sign'
& $java -jar (Join-Path $BuildTools 'lib/apksigner.jar') verify --verbose $Output
Check 'apksigner verify'
Write-Output "APK_READY $Output"
