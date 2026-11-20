param([switch]$Checks, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = '1.0.3405.78'
$packages = Join-Path $root '.packages'
$archive = Join-Path $packages "microsoft.web.webview2.$version.nupkg"
$digest = 'D035807B2AABA871E8C014759626F566E96934E6CE6F0587056EE81D5228C373'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Install .NET Framework 4.8 x64 before building.' }
if (!(Test-Path -LiteralPath $archive)) {
    [void][IO.Directory]::CreateDirectory($packages)
    $pending = "$archive.$([Guid]::NewGuid().ToString('N')).pending"
    try {
        $url = "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$version/microsoft.web.webview2.$version.nupkg"
        Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $pending
        if ((Get-FileHash -LiteralPath $pending).Hash -ne $digest) { throw 'WebView2 package checksum mismatch.' }
        if (!(Test-Path -LiteralPath $archive)) { Move-Item -LiteralPath $pending -Destination $archive }
    } finally {
        if (Test-Path -LiteralPath $pending) { Remove-Item -LiteralPath $pending }
    }
}
if ((Get-FileHash -LiteralPath $archive).Hash -ne $digest) { throw 'WebView2 package checksum mismatch.' }
$out = Join-Path $root 'dist'
if ($Checks) { $out = Join-Path $root '.checks\bin' }
if ($OutputDirectory) {
    $out = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $root $OutputDirectory }
}
[void][IO.Directory]::CreateDirectory((Join-Path $out 'web'))
$core = Join-Path $out 'Microsoft.Web.WebView2.Core.dll'
$wpf = Join-Path $out 'Microsoft.Web.WebView2.Wpf.dll'
$members = @{
    'lib/net462/Microsoft.Web.WebView2.Core.dll' = $core
    'lib/net462/Microsoft.Web.WebView2.Wpf.dll' = $wpf
    'runtimes/win-x64/native/WebView2Loader.dll' = Join-Path $out 'WebView2Loader.dll'
    'LICENSE.txt' = Join-Path $out 'WebView2-LICENSE.txt'
    'NOTICE.txt' = Join-Path $out 'WebView2-NOTICE.txt'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$package = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($path in $members.Keys) {
        [IO.Compression.ZipFileExtensions]::ExtractToFile($package.GetEntry($path), $members[$path], $true)
    }
} finally { $package.Dispose() }
Copy-Item -LiteralPath (Join-Path $root 'ui\embed.html'),(Join-Path $root 'ui\embed.js') -Destination (Join-Path $out 'web') -Force
if ($Checks) { Copy-Item -LiteralPath (Join-Path $root 'assets\example.webm') -Destination $out -Force }
$target = if ($Checks) { 'exe' } else { 'winexe' }
$name = if ($Checks) { 'GuideChecks.exe' } else { 'TeyvatGuide.exe' }
$entry = if ($Checks) { 'TeyvatGuide.Checks' } else { 'TeyvatGuide.Program' }
$opts = @('/nologo','/optimize+','/platform:x64','/codepage:65001',"/target:$target","/main:$entry",
    ("/out:" + (Join-Path $out $name)), ("/win32manifest:" + (Join-Path $root 'src\app.manifest')),
    ("/win32icon:" + (Join-Path $root 'assets\guide.ico')), "/r:$core", "/r:$wpf")
foreach ($asm in @('System','System.Core','System.Drawing','System.Xaml','System.Windows.Forms','System.Net.Http','System.Web','System.Web.Extensions')) {
    $opts += "/r:" + (Join-Path $framework "$asm.dll")
}
foreach ($asm in @('PresentationCore','PresentationFramework','WindowsBase')) {
    $opts += "/r:" + (Join-Path $framework "WPF\$asm.dll")
}
foreach ($resource in @('ui\Main.xaml','ui\Theme.xaml','src\media.js','assets\travel-banner.jpg')) {
    $opts += "/resource:" + (Join-Path $root $resource) + "," + [IO.Path]::GetFileName($resource)
}
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName)
if ($Checks) {
    $sources += (Join-Path $root 'tests\Checks.cs'), (Join-Path $root 'tests\GameLaunchChecks.cs'), (Join-Path $root 'tests\GameKeyChecks.cs')
    $opts += '/r:' + (Join-Path $framework 'WPF\UIAutomationClient.dll')
    $opts += '/r:' + (Join-Path $framework 'WPF\UIAutomationTypes.dll')
}
& $compiler @opts @sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
if ($Checks) {
    $probe = Join-Path $out 'GameProbe.exe'
    & $compiler /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$probe" ("/win32manifest:" + (Join-Path $root 'src\app.manifest')) "/r:$framework\System.Windows.Forms.dll" "/r:$framework\System.Drawing.dll" (Join-Path $root 'tests\GameProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Launch fixture compilation failed.' }
}
Write-Output (Join-Path $out $name)
if (!$Checks) {
    Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $out -Force
    $files = @($members.Values) + @('web','README.md',$name | ForEach-Object { Join-Path $out $_ })
    $portable = Join-Path $out 'TeyvatGuide-win-x64.zip'
    Compress-Archive -LiteralPath $files -DestinationPath $portable -Force
    Write-Output $portable
}
