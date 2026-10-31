$i=[IO.File]::OpenRead("$PSScriptRoot\bh33.cs.gz"); $g=New-Object IO.Compression.GZipStream($i,[IO.Compression.CompressionMode]::Decompress); $o=[IO.File]::Create("$PSScriptRoot\BongHub.cs"); $g.CopyTo($o); $o.Close(); $g.Close(); $i.Close()
& "$PSScriptRoot\devb.ps1"
