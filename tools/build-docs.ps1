# Renders docs\manual-*.md into self-contained HTML hand-outs.
#
# Self-contained on purpose: images are embedded as data URIs, so the result is ONE file that can
# be e-mailed, opened offline on any machine, and printed to PDF straight from the browser
# (Ctrl+P -> Save as PDF). No Word, no PDF toolchain, nothing to install - which is the whole
# reason the manual source is Markdown in the repo: it stays diffable and reviewable in git.
#
# Deliberately a small converter rather than a dependency: the manuals use one known subset of
# Markdown (headings, tables, lists, quotes, code, bold, links, images, rules) and that subset
# is fully covered below.
#
# ASCII only (PS 5.1 reads a BOM-less .ps1 as ANSI).
#
#   powershell -NoProfile -File tools\build-docs.ps1

[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$docs = Join-Path $repo 'docs'

$css = @'
:root{--ink:#1d2733;--muted:#5b6b7c;--line:#dfe6ee;--accent:#1b3a57;--warn:#f39c12;--bg:#fff}
*{box-sizing:border-box}
body{margin:0;background:#eef2f6;color:var(--ink);
 font:16px/1.65 "Segoe UI",-apple-system,Roboto,Helvetica,Arial,sans-serif}
.page{max-width:900px;margin:0 auto;background:var(--bg);padding:64px 72px;
 box-shadow:0 1px 3px rgba(20,40,60,.12)}
h1{font-size:2.1rem;line-height:1.2;margin:0 0 .2em;color:var(--accent);letter-spacing:-.01em}
h2{font-size:1.45rem;margin:2.4em 0 .6em;padding-bottom:.3em;border-bottom:2px solid var(--line);
 color:var(--accent)}
h3{font-size:1.13rem;margin:1.9em 0 .5em;color:var(--accent)}
h4{font-size:1rem;margin:1.5em 0 .4em}
p{margin:.7em 0}
a{color:#1668b3;text-decoration:none}a:hover{text-decoration:underline}
code{background:#f2f5f8;border:1px solid var(--line);border-radius:3px;padding:.08em .35em;
 font:.88em/1.5 Consolas,"Courier New",monospace}
pre{background:#f7f9fb;border:1px solid var(--line);border-left:3px solid var(--accent);
 border-radius:4px;padding:14px 16px;overflow-x:auto}
pre code{background:none;border:0;padding:0}
table{border-collapse:collapse;width:100%;margin:1.1em 0;font-size:.94rem}
th,td{border:1px solid var(--line);padding:9px 12px;text-align:left;vertical-align:top}
th{background:#f4f7fa;font-weight:600;color:var(--accent)}
tr:nth-child(even) td{background:#fafcfd}
blockquote{margin:1.2em 0;padding:12px 18px;background:#fffaf0;border-left:4px solid var(--warn);
 border-radius:0 4px 4px 0}
blockquote p{margin:.3em 0}
img{max-width:100%;height:auto;border:1px solid var(--line);border-radius:4px;margin:.6em 0}
hr{border:0;border-top:1px solid var(--line);margin:2.4em 0}
ul,ol{padding-left:1.5em}li{margin:.32em 0}
em.caption{display:block;color:var(--muted);font-size:.89rem;margin:-.2em 0 1.4em}
.subtitle{color:var(--muted);font-size:1.02rem;margin:0 0 2em}
@media print{
 body{background:#fff}
 .page{max-width:none;padding:0;box-shadow:none}
 h2{page-break-after:avoid}table,pre,blockquote,img{page-break-inside:avoid}
 a{color:inherit;text-decoration:none}
}
'@

function Convert-Inline([string] $s) {
    $s = $s -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;'
    # code first, so nothing inside it gets treated as markup
    $s = [regex]::Replace($s, '`([^`]+)`', { '<code>' + $args[0].Groups[1].Value + '</code>' })
    $s = [regex]::Replace($s, '!\[([^\]]*)\]\(([^)]+)\)', { '<img alt="' + $args[0].Groups[1].Value + '" src="' + $args[0].Groups[2].Value + '">' })
    $s = [regex]::Replace($s, '\[([^\]]+)\]\(([^)]+)\)', { '<a href="' + $args[0].Groups[2].Value + '">' + $args[0].Groups[1].Value + '</a>' })
    $s = [regex]::Replace($s, '\*\*([^*]+)\*\*', { '<strong>' + $args[0].Groups[1].Value + '</strong>' })
    $s = [regex]::Replace($s, '(?<![\w*])\*([^*]+)\*(?![\w*])', { '<em>' + $args[0].Groups[1].Value + '</em>' })
    return $s
}

function Convert-Markdown([string[]] $lines, [string] $baseDir) {
    $out = New-Object Text.StringBuilder
    $inCode = $false; $listType = $null; $inQuote = $false; $inTable = $false
    function CloseBlocks {
        if ($script:listType) { [void]$out.AppendLine("</$script:listType>"); $script:listType = $null }
        if ($script:inQuote)  { [void]$out.AppendLine('</blockquote>');       $script:inQuote  = $false }
        if ($script:inTable)  { [void]$out.AppendLine('</tbody></table>');    $script:inTable  = $false }
    }
    $script:listType = $null; $script:inQuote = $false; $script:inTable = $false

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $ln = $lines[$i]

        if ($ln -match '^```') {
            if ($inCode) { [void]$out.AppendLine('</code></pre>'); $inCode = $false }
            else { CloseBlocks; [void]$out.AppendLine('<pre><code>'); $inCode = $true }
            continue
        }
        if ($inCode) {
            [void]$out.AppendLine(($ln -replace '&','&amp;' -replace '<','&lt;' -replace '>','&gt;'))
            continue
        }
        if ($ln.Trim() -eq '') { CloseBlocks; continue }

        # table: a header row followed by a |---| separator
        if ($ln -match '^\s*\|' -and $i + 1 -lt $lines.Count -and $lines[$i+1] -match '^\s*\|[\s:|-]+\|\s*$') {
            CloseBlocks
            $cells = ($ln.Trim().Trim('|') -split '\|') | ForEach-Object { Convert-Inline $_.Trim() }
            [void]$out.AppendLine('<table><thead><tr>' + (($cells | ForEach-Object { "<th>$_</th>" }) -join '') + '</tr></thead><tbody>')
            $script:inTable = $true; $i++
            continue
        }
        if ($script:inTable -and $ln -match '^\s*\|') {
            $cells = ($ln.Trim().Trim('|') -split '\|') | ForEach-Object { Convert-Inline $_.Trim() }
            [void]$out.AppendLine('<tr>' + (($cells | ForEach-Object { "<td>$_</td>" }) -join '') + '</tr>')
            continue
        }

        if ($ln -match '^(#{1,4})\s+(.*)$') {
            CloseBlocks
            $lvl = $matches[1].Length
            $txt = Convert-Inline $matches[2]
            $id  = ($matches[2] -replace '[^\w\s-]','').Trim().ToLower() -replace '\s+','-'
            [void]$out.AppendLine("<h$lvl id=`"$id`">$txt</h$lvl>")
            continue
        }
        if ($ln -match '^---+\s*$') { CloseBlocks; [void]$out.AppendLine('<hr>'); continue }

        if ($ln -match '^>\s?(.*)$') {
            if (-not $script:inQuote) { CloseBlocks; [void]$out.AppendLine('<blockquote>'); $script:inQuote = $true }
            [void]$out.AppendLine('<p>' + (Convert-Inline $matches[1]) + '</p>')
            continue
        }
        if ($ln -match '^\s*[-*]\s+(.*)$') {
            if ($script:listType -ne 'ul') { CloseBlocks; [void]$out.AppendLine('<ul>'); $script:listType = 'ul' }
            [void]$out.AppendLine('<li>' + (Convert-Inline $matches[1]) + '</li>')
            continue
        }
        if ($ln -match '^\s*\d+\.\s+(.*)$') {
            if ($script:listType -ne 'ol') { CloseBlocks; [void]$out.AppendLine('<ol>'); $script:listType = 'ol' }
            [void]$out.AppendLine('<li>' + (Convert-Inline $matches[1]) + '</li>')
            continue
        }

        # a whole line in italics right after an image reads as its caption
        if ($ln -match '^\*([^*].*)\*$') {
            CloseBlocks
            [void]$out.AppendLine('<em class="caption">' + (Convert-Inline $matches[1]) + '</em>')
            continue
        }

        CloseBlocks
        [void]$out.AppendLine('<p>' + (Convert-Inline $ln) + '</p>')
    }
    CloseBlocks
    if ($inCode) { [void]$out.AppendLine('</code></pre>') }
    return $out.ToString()
}

function Embed-Images([string] $html, [string] $baseDir) {
    return [regex]::Replace($html, 'src="([^"h][^"]*)"', {
        param($m)
        $rel = $m.Groups[1].Value
        $p = Join-Path $baseDir $rel
        if (-not (Test-Path $p)) { Write-Host "  (missing image: $rel)" -ForegroundColor Yellow; return $m.Value }
        $ext = [IO.Path]::GetExtension($p).TrimStart('.').ToLower()
        if ($ext -eq 'jpg') { $ext = 'jpeg' }
        $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($p))
        return 'src="data:image/' + $ext + ';base64,' + $b64 + '"'
    })
}

$jobs = @(
    @{ Src = 'manual-en.md'; Out = 'Manual-EN.html';   Title = 'Historian Data Sync - User Manual' },
    @{ Src = 'manual-de.md'; Out = 'Handbuch-DE.html'; Title = 'Historian Data Sync - Handbuch' }
)

foreach ($j in $jobs) {
    $src = Join-Path $docs $j.Src
    if (-not (Test-Path $src)) { Write-Host "skipped (no $($j.Src))" -ForegroundColor Yellow; continue }
    Write-Host "Rendering $($j.Src) ..." -ForegroundColor Cyan
    $body = Convert-Markdown (Get-Content $src -Encoding UTF8) $docs
    $body = Embed-Images $body $docs
    $html = @"
<!doctype html>
<html lang="$(if ($j.Src -match '-de') { 'de' } else { 'en' })">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>$($j.Title)</title>
<style>$css</style>
</head>
<body><div class="page">
$body
</div></body>
</html>
"@
    $dest = Join-Path $docs $j.Out
    [IO.File]::WriteAllText($dest, $html, (New-Object Text.UTF8Encoding $false))
    $kb = [Math]::Round((Get-Item $dest).Length / 1KB)
    Write-Host "  -> $($j.Out)  ($kb KB, self-contained)" -ForegroundColor Green
}

Write-Host ""
Write-Host "Open in a browser and print to PDF if a PDF is needed (Ctrl+P -> Save as PDF)."
