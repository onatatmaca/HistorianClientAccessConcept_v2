param(
  [string]$Template = "C:\Users\OAtmaca\Downloads\0. Die Doku_Funktionen_Portal_2017....docx",
  [string]$Published = "05.08.2026",
  [string]$Author = "ORmatiC GmbH"
)
# Builds Handbuch-DE.docx and Manual-EN.docx in the SAME layout as the ORmatic portal manual.
#
# It does not imitate that document - it REUSES it. The template is unzipped and only the body
# (word/document.xml and its relationships) is replaced; styles.xml, numbering.xml, the theme,
# the header with the ORmatic logo and the page-number footer are carried over untouched. So the
# fonts, the heading numbering (1, 4.1, 4.2), the caption style and the header/footer are
# identical by construction rather than by eye, and the table of contents is a real Word TOC
# field with real page numbers - which HTML cannot produce.
#
# Content comes from the same docs\manual-*.md sources as the HTML build, so there is one
# manual, not two that drift apart.
#
# ASCII only (PS 5.1 reads a BOM-less .ps1 as ANSI).
#
#   powershell -NoProfile -File tools\build-docx.ps1

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$docs = Join-Path $repo 'docs'
$work = Join-Path $env:TEMP ("docxbuild_" + [Guid]::NewGuid().ToString('N').Substring(0,8))

if (-not (Test-Path $Template)) { throw "Template not found: $Template" }
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Drawing

# ---------------------------------------------------------------- helpers
function Esc([string]$s){ ($s -replace '&','&amp;' -replace '<','&lt;' -replace '>','&gt;') }

# Word runs: **bold**, `code`, [text](url) -> text. Everything else is literal.
function Runs([string]$text){
    $sb = New-Object Text.StringBuilder
    $text = [regex]::Replace($text, '\[([^\]]+)\]\(([^)]+)\)', '$1')
    $text = [regex]::Replace($text, '!\[[^\]]*\]\([^)]+\)', '')
    # split on bold / code, keeping the delimiters
    $parts = [regex]::Split($text, '(\*\*[^*]+\*\*|`[^`]+`)')
    foreach($p in $parts){
        if(-not $p){ continue }
        if($p -match '^\*\*(.+)\*\*$'){
            [void]$sb.Append('<w:r><w:rPr><w:b/></w:rPr><w:t xml:space="preserve">' + (Esc $matches[1]) + '</w:t></w:r>')
        } elseif($p -match '^`(.+)`$'){
            [void]$sb.Append('<w:r><w:rPr><w:rFonts w:ascii="Consolas" w:hAnsi="Consolas"/><w:sz w:val="18"/></w:rPr><w:t xml:space="preserve">' + (Esc $matches[1]) + '</w:t></w:r>')
        } else {
            [void]$sb.Append('<w:r><w:t xml:space="preserve">' + (Esc $p) + '</w:t></w:r>')
        }
    }
    return $sb.ToString()
}
function Para([string]$text, [string]$style = $null, [switch]$Italic, [switch]$Center){
    $pPr = '<w:pPr>'
    if($style){ $pPr += '<w:pStyle w:val="' + $style + '"/>' }
    if($Center){ $pPr += '<w:jc w:val="center"/>' }
    if($Italic){ $pPr += '<w:rPr><w:i/></w:rPr>' }
    $pPr += '</w:pPr>'
    $r = Runs $text
    if($Italic){ $r = $r -replace '<w:r><w:t','<w:r><w:rPr><w:i/></w:rPr><w:t' }
    return '<w:p>' + $pPr + $r + '</w:p>'
}

$script:imgRels = @{}   # file -> rId
$script:nextImg = 100
$script:docPrId = 1000
function ImageParagraph([string]$file, [int]$maxWidthEmu){
    if(-not (Test-Path $file)){ Write-Host "  (missing image $file)" -ForegroundColor Yellow; return '' }
    $key = [IO.Path]::GetFileName($file)
    if(-not $script:imgRels.ContainsKey($key)){
        $script:nextImg++
        $script:imgRels[$key] = "rId$($script:nextImg)"
        $dst = Join-Path $work "word\media\$key"
        # the header logo is already in place - copying it onto itself is an error
        if([IO.Path]::GetFullPath($file) -ne [IO.Path]::GetFullPath($dst)){ Copy-Item $file $dst -Force }
    }
    $rid = $script:imgRels[$key]
    $img = [System.Drawing.Image]::FromFile($file)
    $w = $img.Width; $h = $img.Height; $img.Dispose()
    $cx = $maxWidthEmu
    $cy = [int]([double]$cx * $h / $w)
    $script:docPrId++
    $id = $script:docPrId
    return @"
<w:p><w:pPr><w:jc w:val="center"/></w:pPr><w:r><w:drawing>
<wp:inline distT="0" distB="0" distL="0" distR="0" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing">
<wp:extent cx="$cx" cy="$cy"/><wp:effectExtent l="0" t="0" r="0" b="0"/>
<wp:docPr id="$id" name="Picture $id"/><wp:cNvGraphicFramePr/>
<a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
<a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
<pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
<pic:nvPicPr><pic:cNvPr id="$id" name="$key"/><pic:cNvPicPr/></pic:nvPicPr>
<pic:blipFill><a:blip r:embed="$rid" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
<pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="$cx" cy="$cy"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>
</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>
"@
}

function TableXml([string[]]$header, [System.Collections.ArrayList]$rows){
    $tw = 9070   # content width in twips (A4 minus the template's margins)
    $cols = $header.Count
    $cw = [int]($tw / $cols)
    $sb = New-Object Text.StringBuilder
    [void]$sb.Append('<w:tbl><w:tblPr><w:tblW w:w="' + $tw + '" w:type="dxa"/><w:tblBorders>')
    foreach($e in @('top','left','bottom','right','insideH','insideV')){
        [void]$sb.Append('<w:' + $e + ' w:val="single" w:sz="4" w:space="0" w:color="BFBFBF"/>')
    }
    [void]$sb.Append('</w:tblBorders><w:tblCellMar><w:top w:w="60" w:type="dxa"/><w:left w:w="90" w:type="dxa"/><w:bottom w:w="60" w:type="dxa"/><w:right w:w="90" w:type="dxa"/></w:tblCellMar></w:tblPr><w:tblGrid>')
    for($i=0;$i -lt $cols;$i++){ [void]$sb.Append('<w:gridCol w:w="' + $cw + '"/>') }
    [void]$sb.Append('</w:tblGrid>')
    # header row: shaded and repeated on every page
    [void]$sb.Append('<w:tr><w:trPr><w:tblHeader/></w:trPr>')
    foreach($h in $header){
        [void]$sb.Append('<w:tc><w:tcPr><w:tcW w:w="' + $cw + '" w:type="dxa"/><w:shd w:val="clear" w:color="auto" w:fill="EAEFF4"/></w:tcPr>' +
            '<w:p><w:pPr><w:rPr><w:b/></w:rPr></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t xml:space="preserve">' + (Esc ($h -replace '\*\*','')) + '</w:t></w:r></w:p></w:tc>')
    }
    [void]$sb.Append('</w:tr>')
    foreach($row in $rows){
        [void]$sb.Append('<w:tr>')
        for($i=0;$i -lt $cols;$i++){
            $cell = if($i -lt $row.Count){ $row[$i] } else { '' }
            [void]$sb.Append('<w:tc><w:tcPr><w:tcW w:w="' + $cw + '" w:type="dxa"/></w:tcPr><w:p>' + (Runs $cell) + '</w:p></w:tc>')
        }
        [void]$sb.Append('</w:tr>')
    }
    [void]$sb.Append('</w:tbl><w:p/>')
    return $sb.ToString()
}

# ---------------------------------------------------------------- markdown -> body
function ConvertBody([string[]]$lines, [string]$lang){
    $contentEmu = 5760000
    $sb = New-Object Text.StringBuilder
    $inCode = $false; $codeBuf = @()
    $skipToc = $false
    $figure = 0

    for($i=0; $i -lt $lines.Count; $i++){
        $ln = $lines[$i]

        if($ln -match '^```'){
            if($inCode){
                foreach($c in $codeBuf){
                    [void]$sb.Append('<w:p><w:pPr><w:shd w:val="clear" w:color="auto" w:fill="F4F6F8"/><w:spacing w:after="0"/></w:pPr><w:r><w:rPr><w:rFonts w:ascii="Consolas" w:hAnsi="Consolas"/><w:sz w:val="18"/></w:rPr><w:t xml:space="preserve">' + (Esc $c) + '</w:t></w:r></w:p>')
                }
                [void]$sb.Append('<w:p/>'); $codeBuf=@(); $inCode=$false
            } else { $inCode = $true }
            continue
        }
        if($inCode){ $codeBuf += $ln; continue }

        if($ln.Trim() -eq ''){ continue }
        if($ln -match '^---+$'){ continue }

        # The markdown carries its own Contents list; Word builds a real one, so drop it.
        if($ln -match '^##\s+(Contents|Inhaltsverzeichnis)\s*$'){ $skipToc = $true; continue }
        if($skipToc){
            if($ln -match '^#'){ $skipToc = $false } else { continue }
        }

        if($ln -match '^(#{1,4})\s+(.*)$'){
            $lvl = $matches[1].Length
            $txt = $matches[2]
            if($lvl -eq 1){ continue }          # the document title lives on the cover page
            # Word numbers the headings itself, so strip the numbers written in the markdown.
            $txt = $txt -replace '^\d+(\.\d+)*\.?\s+',''
            $style = "berschrift" + ([Math]::Min(3, $lvl - 1))
            [void]$sb.Append((Para $txt $style))
            continue
        }

        # image, optionally followed by an italic caption line
        if($ln -match '^!\[([^\]]*)\]\(([^)]+)\)'){
            $rel = $matches[2]
            [void]$sb.Append((ImageParagraph (Join-Path $docs $rel) $contentEmu))
            $figure++
            if($i+1 -lt $lines.Count -and $lines[$i+1] -match '^\*(.+)\*$'){
                $cap = $matches[1]
                $i++
                [void]$sb.Append((Para ("{0} {1}" -f $figure, $cap) 'Beschriftung'))
            }
            continue
        }

        # table
        if($ln -match '^\s*\|' -and $i+1 -lt $lines.Count -and $lines[$i+1] -match '^\s*\|[\s:|-]+\|\s*$'){
            $header = ($ln.Trim().Trim('|') -split '\|') | ForEach-Object { $_.Trim() }
            $i += 2
            $rows = New-Object System.Collections.ArrayList
            while($i -lt $lines.Count -and $lines[$i] -match '^\s*\|'){
                [void]$rows.Add((($lines[$i].Trim().Trim('|') -split '\|') | ForEach-Object { $_.Trim() }))
                $i++
            }
            $i--
            [void]$sb.Append((TableXml $header $rows))
            continue
        }

        if($ln -match '^>\s?(.*)$'){
            # pBdr BEFORE ind: the children of w:pPr have a fixed schema order (pStyle, numPr,
            # pBdr, shd, spacing, ind, jc, rPr). Word rejects the whole document as corrupt if
            # they are out of order, with no hint as to which element is at fault.
            [void]$sb.Append('<w:p><w:pPr><w:pBdr><w:left w:val="single" w:sz="18" w:space="8" w:color="F39C12"/></w:pBdr><w:ind w:left="284"/></w:pPr>' + (Runs $matches[1]) + '</w:p>')
            continue
        }
        if($ln -match '^\s*[-*]\s+(.*)$'){
            # $script:bulletNumId, NOT a guessed 1. numId 1 in this template is the multilevel
            # list that NUMBERS THE HEADINGS, so every bullet advanced the chapter counter and
            # the contents read 1, 2, 3, 4, 10, 11, 14.1, 17.1 ...
            # Plain indented paragraph with a literal bullet - deliberately NOT the Listenabsatz
            # style and NOT a numPr. In this template both are wired to the multilevel list that
            # numbers the HEADINGS, so every list item advanced the chapter counter and the
            # contents read 1, 2, 3, 4, 10, 11, 14.1 instead of 1..6.
            [void]$sb.Append('<w:p><w:pPr><w:ind w:left="454" w:hanging="227"/><w:spacing w:after="60"/></w:pPr><w:r><w:t xml:space="preserve">' + [char]0x2022 + '   </w:t></w:r>' + (Runs $matches[1]) + '</w:p>')
            continue
        }
        if($ln -match '^\s*\d+\.\s+(.*)$'){
            # Same reason: the markdown already carries its own "1." "2." numbers, so they are
            # kept as literal text and Word is not asked to number anything.
            [void]$sb.Append('<w:p><w:pPr><w:ind w:left="454" w:hanging="227"/><w:spacing w:after="60"/></w:pPr>' + (Runs $ln.Trim()) + '</w:p>')
            continue
        }
        if($ln -match '^\*([^*].*)\*$'){
            [void]$sb.Append((Para $matches[1] $null -Italic))
            continue
        }
        [void]$sb.Append((Para $ln))
    }
    return $sb.ToString()
}

function CoverAndToc([string]$lang, [string]$title){
    # The clean logo supplied for the cover; the template's own small header logo (image46)
    # keeps running in the page header, exactly as in the original document.
    $logo = Join-Path $docs 'img\ormatic-logo.png'
    if(-not (Test-Path $logo)){ $logo = Join-Path $work 'word\media\image46.jpeg' }
    $sb = New-Object Text.StringBuilder
    [void]$sb.Append('<w:p/><w:p/>')
    [void]$sb.Append((ImageParagraph $logo 2200000))
    [void]$sb.Append('<w:p/><w:p/>')
    $docWord = if($lang -eq 'de'){ 'Dokumentation' } else { 'Documentation' }
    $forWord = if($lang -eq 'de'){ 'für' } else { 'for' }
    foreach($t in @($docWord, $forWord)){
        [void]$sb.Append('<w:p><w:pPr><w:pStyle w:val="KeinLeerraum"/><w:jc w:val="center"/></w:pPr><w:r><w:rPr><w:sz w:val="36"/></w:rPr><w:t xml:space="preserve">' + (Esc $t) + '</w:t></w:r></w:p>')
    }
    [void]$sb.Append('<w:p><w:pPr><w:pStyle w:val="KeinLeerraum"/><w:jc w:val="center"/></w:pPr><w:r><w:rPr><w:b/><w:sz w:val="52"/></w:rPr><w:t xml:space="preserve">' + (Esc $title) + '</w:t></w:r></w:p>')
    [void]$sb.Append('<w:p/><w:p/><w:p/><w:p/>')
    $rows = if($lang -eq 'de'){
        @(@('Bearbeiter:', $Author), @('Veröffentlicht:', $Published), @('Letzte Fassung', $Published), @('Version', '2.1'))
    } else {
        @(@('Author:', $Author), @('Published:', $Published), @('Last revision', $Published), @('Version', '2.1'))
    }
    foreach($r in $rows){
        [void]$sb.Append('<w:p><w:pPr><w:pStyle w:val="KeinLeerraum"/><w:jc w:val="center"/></w:pPr>' +
          '<w:r><w:rPr><w:b/></w:rPr><w:t xml:space="preserve">' + (Esc $r[0]) + '  </w:t></w:r>' +
          '<w:r><w:t xml:space="preserve">' + (Esc $r[1]) + '</w:t></w:r></w:p>')
    }
    # page break, then the table of contents as chapter 1 - exactly as in the template
    [void]$sb.Append('<w:p><w:r><w:br w:type="page"/></w:r></w:p>')
    $tocTitle = if($lang -eq 'de'){ 'Inhaltsverzeichnis' } else { 'Contents' }
    [void]$sb.Append((Para $tocTitle 'berschrift1'))
    $hint = if($lang -eq 'de'){ 'Verzeichnis wird beim Oeffnen aktualisiert (sonst Strg+A, F9).' } else { 'This table is updated when the document opens (otherwise Ctrl+A, F9).' }
    [void]$sb.Append('<w:p><w:r><w:fldChar w:fldCharType="begin" w:dirty="true"/></w:r>' +
      '<w:r><w:instrText xml:space="preserve"> TOC \o "1-3" \h \z \u </w:instrText></w:r>' +
      '<w:r><w:fldChar w:fldCharType="separate"/></w:r>' +
      '<w:r><w:t xml:space="preserve">' + (Esc $hint) + '</w:t></w:r>' +
      '<w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>')
    [void]$sb.Append('<w:p><w:r><w:br w:type="page"/></w:r></w:p>')
    return $sb.ToString()
}

# ---------------------------------------------------------------- build one document
function BuildDoc([string]$mdFile, [string]$outFile, [string]$lang, [string]$title){
    if(Test-Path $work){ Remove-Item $work -Recurse -Force }
    New-Item -ItemType Directory $work -Force | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($Template, $work)

    # keep only the header logo; the template's own 45 screenshots are not ours to ship
    Get-ChildItem (Join-Path $work 'word\media') -File |
        Where-Object { $_.Name -ne 'image46.jpeg' } | Remove-Item -Force

    $script:imgRels = @{}; $script:nextImg = 100; $script:docPrId = 1000

    # Find a list that really is a BULLET list. Guessing an id picks the heading numbering and
    # silently corrupts every chapter number in the document and its table of contents.
    $numXml = [IO.File]::ReadAllText((Join-Path $work 'word\numbering.xml'))
    $bulletAbstracts = @{}
    foreach($m in [regex]::Matches($numXml, '<w:abstractNum[^>]*w:abstractNumId="(\d+)"[\s\S]*?</w:abstractNum>')){
        $first = [regex]::Match($m.Value, '<w:lvl [^>]*w:ilvl="0"[\s\S]*?</w:lvl>')
        if($first.Success -and $first.Value -match '<w:numFmt w:val="bullet"/>'){ $bulletAbstracts[$m.Groups[1].Value] = $true }
    }
    $script:bulletNumId = $null
    foreach($m in [regex]::Matches($numXml, '<w:num w:numId="(\d+)"[^>]*>\s*<w:abstractNumId w:val="(\d+)"')){
        if($bulletAbstracts.ContainsKey($m.Groups[2].Value)){ $script:bulletNumId = $m.Groups[1].Value; break }
    }
    if(-not $script:bulletNumId){ throw "No bullet list found in numbering.xml - refusing to guess a numId." }

    $body = CoverAndToc $lang $title
    $body += ConvertBody (Get-Content (Join-Path $docs $mdFile) -Encoding UTF8) $lang

    # A4 with the template's margins, titlePg so the cover has no header/footer, and the
    # template's own header (ORmatic logo + current chapter) and page-number footer.
    # rId58 / rId60 are the template's OWN header and footer relationships, reused as-is.
    $sect = '<w:sectPr><w:headerReference w:type="default" r:id="rId58"/><w:footerReference w:type="default" r:id="rId60"/>' +
            '<w:pgSz w:w="11907" w:h="16839" w:code="9"/>' +
            '<w:pgMar w:top="1417" w:right="1417" w:bottom="1134" w:left="1417" w:header="708" w:footer="708" w:gutter="0"/>' +
            '<w:cols w:space="708"/><w:titlePg/><w:docGrid w:linePitch="360"/></w:sectPr>'

    $doc = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
      '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" ' +
      'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" ' +
      'xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" ' +
      'xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" ' +
      'xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">' +
      '<w:body>' + $body + $sect + '</w:body></w:document>'
    [IO.File]::WriteAllText((Join-Path $work 'word\document.xml'), $doc, (New-Object Text.UTF8Encoding $false))

    # PATCH the template's relationships; do NOT rewrite them.
    #
    # A hand-written rels file listing only the parts that look necessary produces a document
    # Word rejects outright ("Die Datei ist moeglicherweise beschaedigt"). Bisected: the same
    # body with the template's ORIGINAL rels opens fine. The package carries relationships that
    # are easy to assume unnecessary - customXml, footnotes, endnotes, stylesWithEffects - and
    # dropping them breaks the file. So keep every original relationship, remove only the image
    # ones whose media we deleted, and append ours.
    $relPath = Join-Path $work 'word\_rels\document.xml.rels'
    $rels = [IO.File]::ReadAllText($relPath)
    $rels = [regex]::Replace($rels, '<Relationship[^>]*/image"[^>]*/>', '')
    $add = ''
    foreach($kv in $script:imgRels.GetEnumerator()){
        $add += '<Relationship Id="' + $kv.Value + '" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/' + $kv.Key + '"/>'
    }
    $rels = $rels -replace '</Relationships>', ($add + '</Relationships>')
    [IO.File]::WriteAllText($relPath, $rels, (New-Object Text.UTF8Encoding $false))

    # header2 stays in the package: unreferenced but harmless. Removing a part also means
    # removing its content-type override and its own rels - more ways to break the file than
    # it is worth.

    # PNG needs a content type, and Word must refresh the TOC when the file opens
    # NOT Get-Content: the square brackets in [Content_Types].xml are a wildcard to PowerShell's
    # path parser, so the file is never found.
    $ctPath = Join-Path $work '[Content_Types].xml'
    $ct = [IO.File]::ReadAllText($ctPath)
    if($ct -notmatch 'Extension="png"'){
        $ct = $ct -replace '(<Types[^>]*>)', '$1<Default Extension="png" ContentType="image/png"/>'
    }
    [IO.File]::WriteAllText($ctPath, $ct, (New-Object Text.UTF8Encoding $false))

    $setPath = Join-Path $work 'word\settings.xml'
    $set = [IO.File]::ReadAllText($setPath)
    if($set -notmatch 'updateFields'){
        $set = $set -replace '(<w:settings[^>]*>)', '$1<w:updateFields w:val="true"/>'
    }
    [IO.File]::WriteAllText($setPath, $set, (New-Object Text.UTF8Encoding $false))

    # --- validate BEFORE zipping ----------------------------------------------------------
    # Word reports any structural mistake as one unhelpful "the file may be corrupt", so check
    # the two things that actually break: XML that does not parse, and a manifest or
    # relationship that points at a part which is not in the package.
    foreach($xml in (Get-ChildItem $work -Recurse -Filter *.xml)){
        try { $probe = New-Object Xml.XmlDocument; $probe.Load($xml.FullName) }
        catch { throw ("Malformed XML in {0}: {1}" -f $xml.FullName.Substring($work.Length+1), $_.Exception.Message) }
    }
    $ctText = [IO.File]::ReadAllText((Join-Path $work '[Content_Types].xml'))
    foreach($m in [regex]::Matches($ctText, 'PartName="/([^"]+)"')){
        $part = Join-Path $work ($m.Groups[1].Value.Replace('/','\'))
        if(-not (Test-Path -LiteralPath $part)){
            throw ("[Content_Types].xml declares a part that is not in the package: {0}" -f $m.Groups[1].Value)
        }
    }
    foreach($relFile in (Get-ChildItem $work -Recurse -Filter *.rels)){
        $base = Split-Path (Split-Path $relFile.FullName -Parent) -Parent
        foreach($m in [regex]::Matches([IO.File]::ReadAllText($relFile.FullName), 'Target="([^"]+)"\s*(TargetMode="External")?')){
            if($m.Groups[2].Success -or $m.Groups[1].Value -match '^https?:'){ continue }
            $tgt = Join-Path $base ($m.Groups[1].Value.Replace('/','\'))
            if(-not (Test-Path -LiteralPath $tgt)){
                throw ("{0} references a missing target: {1}" -f $relFile.Name, $m.Groups[1].Value)
            }
        }
    }

    $dest = Join-Path $docs $outFile
    if(Test-Path $dest){ Remove-Item $dest -Force }
    # Written by hand rather than CreateFromDirectory so [Content_Types].xml is the FIRST entry,
    # which the OPC spec requires and some readers enforce.
    $zip = [IO.Compression.ZipFile]::Open($dest, [IO.Compression.ZipArchiveMode]::Create)
    try{
        $files = @(Join-Path $work '[Content_Types].xml') +
                 (Get-ChildItem $work -Recurse -File |
                    Where-Object { $_.Name -ne '[Content_Types].xml' } |
                    ForEach-Object { $_.FullName })
        foreach($f in $files){
            $entry = $f.Substring($work.Length + 1).Replace('\','/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f, $entry)
        }
    } finally { $zip.Dispose() }
    Remove-Item $work -Recurse -Force
    Write-Host ("  -> {0}  ({1:N0} KB)" -f $outFile, ((Get-Item $dest).Length/1KB)) -ForegroundColor Green
}

Write-Host "Building Word manuals from the ORmatic template ..." -ForegroundColor Cyan
BuildDoc 'manual-de.md' 'Handbuch-DE.docx' 'de' 'Historian Data Sync'
BuildDoc 'manual-en.md' 'Manual-EN.docx'  'en' 'Historian Data Sync'
Write-Host ""
Write-Host "Open in Word. The contents list fills in on open (or Ctrl+A then F9)."
