$packageName = 'markdownmonster'
$fileType = 'exe'
$url = 'https://github.com/RickStrahl/MarkdownMonsterReleases/raw/master/v1.21/MarkdownMonsterSetup-1.22.6.exe'

$silentArgs = '/VERYSILENT'
$validExitCodes = @(0)

Install-ChocolateyPackage "packageName" "$fileType" "$silentArgs" "$url"  -validExitCodes  $validExitCodes  -checksum "0C5D31715C0D60BD32C283BEA673B476F3907D623BA6C71E8E973A68F7CB75C4" -checksumType "sha256"
