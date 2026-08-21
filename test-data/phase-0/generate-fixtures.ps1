[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$sourceDirectory = Join-Path $PSScriptRoot 'sources'
$expectedDirectory = Join-Path $PSScriptRoot 'expected'
New-Item -ItemType Directory -Path $sourceDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $expectedDirectory -Force | Out-Null

$canvasWidth = 1600
$canvasHeight = 1200
$fontFamily = 'Segoe UI'

function Add-Text {
    param(
        [System.Drawing.Graphics]$Graphics,
        [string]$Text,
        [single]$X,
        [single]$Y,
        [single]$Width,
        [single]$Height,
        [single]$Size = 24,
        [System.Drawing.FontStyle]$Style = [System.Drawing.FontStyle]::Regular,
        [System.Drawing.Color]$Color = [System.Drawing.Color]::Black,
        [switch]$RightToLeft,
        [System.Drawing.StringAlignment]$Alignment = [System.Drawing.StringAlignment]::Near
    )

    $font = [System.Drawing.Font]::new($fontFamily, $Size, $Style, [System.Drawing.GraphicsUnit]::Pixel)
    $brush = [System.Drawing.SolidBrush]::new($Color)
    $format = [System.Drawing.StringFormat]::new()
    $format.Alignment = $Alignment
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    if ($RightToLeft) {
        $format.FormatFlags = $format.FormatFlags -bor [System.Drawing.StringFormatFlags]::DirectionRightToLeft
    }

    try {
        $rectangle = [System.Drawing.RectangleF]::new($X, $Y, $Width, $Height)
        $Graphics.DrawString($Text, $font, $brush, $rectangle, $format)
    }
    finally {
        $format.Dispose()
        $brush.Dispose()
        $font.Dispose()
    }
}

function Add-Line {
    param(
        [System.Drawing.Graphics]$Graphics,
        [single]$X1,
        [single]$Y1,
        [single]$X2,
        [single]$Y2,
        [single]$Width = 2
    )

    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(90, 90, 90), $Width)
    try {
        $Graphics.DrawLine($pen, $X1, $Y1, $X2, $Y2)
    }
    finally {
        $pen.Dispose()
    }
}

function New-Canvas {
    $bitmap = [System.Drawing.Bitmap]::new($canvasWidth, $canvasHeight)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::White)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    return [pscustomobject]@{ Bitmap = $bitmap; Graphics = $graphics }
}

function Save-Canvas {
    param(
        $Canvas,
        [string]$FileName
    )

    $path = Join-Path $sourceDirectory $FileName
    try {
        $Canvas.Bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $Canvas.Graphics.Dispose()
        $Canvas.Bitmap.Dispose()
    }
    return $path
}

function Add-SyntheticBanner {
    param(
        [System.Drawing.Graphics]$Graphics,
        [string]$Text,
        [switch]$RightToLeft
    )

    $brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 238, 238))
    try {
        $Graphics.FillRectangle($brush, 0, 0, $canvasWidth, 70)
    }
    finally {
        $brush.Dispose()
    }

    Add-Text -Graphics $Graphics -Text $Text -X 40 -Y 5 -Width 1520 -Height 60 -Size 28 -Style Bold -Color ([System.Drawing.Color]::FromArgb(170, 20, 20)) -RightToLeft:$RightToLeft -Alignment Center
}

function Add-TableGrid {
    param(
        [System.Drawing.Graphics]$Graphics,
        [int[]]$Columns,
        [int[]]$Rows
    )

    foreach ($x in $Columns) {
        Add-Line -Graphics $Graphics -X1 $x -Y1 $Rows[0] -X2 $x -Y2 $Rows[-1]
    }
    foreach ($y in $Rows) {
        Add-Line -Graphics $Graphics -X1 $Columns[0] -Y1 $y -X2 $Columns[-1] -Y2 $y
    }
}

function New-EnglishInvoice {
    $canvas = New-Canvas
    $g = $canvas.Graphics
    Add-SyntheticBanner -Graphics $g -Text 'SYNTHETIC TEST INVOICE — NOT A REAL BUSINESS DOCUMENT'
    Add-Text $g 'Synthetic Pharmacy Supplier EN' 80 95 900 70 42 Bold
    Add-Text $g 'Vendor code: SYNTH-VENDOR-EN' 80 175 650 42 24
    Add-Text $g 'Invoice No: SYN-EN-0001' 1030 110 490 40 26 Bold
    Add-Text $g 'Date: 2026-01-15' 1030 160 490 40 26
    Add-Text $g 'Currency: EGP' 1030 210 490 40 26

    $columns = @(80, 160, 360, 800, 900, 1010, 1120, 1230, 1350, 1520)
    $rows = @(320, 390, 485, 580)
    Add-TableGrid $g $columns $rows
    $headers = @('#', 'Code', 'Description', 'Qty', 'Unit', 'Price', 'D1%', 'D2%', 'Sell/Box')
    for ($i = 0; $i -lt $headers.Count; $i++) {
        Add-Text $g $headers[$i] $columns[$i] 325 ($columns[$i + 1] - $columns[$i]) 55 19 Bold -Alignment Center
    }
    $row1 = @('1', 'TST-001', 'TEST-MED-A 500 mg Capsules | Exp 2028-06 | Batch SYN-BATCH-A', '2', 'BOX', '100.00', '10.00', '5.00', '150.00')
    $row2 = @('2', 'TST-002', 'TEST-SYRUP-B 120 ml | Exp 2027-12 | Batch SYN-BATCH-B', '1', 'BOX', '80.00', '0.00', '0.00', '110.00')
    foreach ($pair in @(@($row1, 395), @($row2, 490))) {
        for ($i = 0; $i -lt $pair[0].Count; $i++) {
            Add-Text $g $pair[0][$i] $columns[$i] $pair[1] ($columns[$i + 1] - $columns[$i]) 85 17 -Alignment Center
        }
    }

    Add-Text $g 'Subtotal: EGP 280.00' 1000 650 500 45 28 Bold -Alignment Far
    Add-Text $g 'Discount: EGP 29.00' 1000 705 500 45 28 -Alignment Far
    Add-Text $g 'Tax: EGP 0.00' 1000 760 500 45 28 -Alignment Far
    Add-Text $g 'Total: EGP 251.00' 1000 825 500 55 34 Bold -Alignment Far
    Add-Text $g 'All names, identifiers, prices, batches and dates on this page were generated from scratch.' 80 1050 1440 45 22 Italic -Color ([System.Drawing.Color]::FromArgb(90, 90, 90)) -Alignment Center
    return Save-Canvas $canvas 'synthetic-en-invoice-001.png'
}

function New-ArabicInvoice {
    $canvas = New-Canvas
    $g = $canvas.Graphics
    Add-SyntheticBanner -Graphics $g -Text 'فاتورة اختبار اصطناعية — ليست مستندًا تجاريًا حقيقيًا' -RightToLeft
    Add-Text $g 'مورد الاختبار الاصطناعي' 620 95 900 70 42 Bold -RightToLeft -Alignment Far
    Add-Text $g 'كود المورد: SYNTH-VENDOR-AR' 800 175 720 42 24 -RightToLeft -Alignment Far
    Add-Text $g 'رقم الفاتورة: SYN-AR-0001' 80 110 600 40 26 Bold -RightToLeft -Alignment Far
    Add-Text $g 'التاريخ: 2026-02-20' 80 160 600 40 26 -RightToLeft -Alignment Far
    Add-Text $g 'العملة: EGP' 80 210 600 40 26 -RightToLeft -Alignment Far

    $columns = @(80, 180, 380, 900, 1030, 1160, 1290, 1420, 1520)
    $rows = @(320, 400, 515)
    Add-TableGrid $g $columns $rows
    $headers = @('م', 'الكود', 'الوصف', 'الكمية', 'الوحدة', 'السعر', 'خصم ١', 'سعر البيع')
    for ($i = 0; $i -lt $headers.Count; $i++) {
        Add-Text $g $headers[$i] $columns[$i] 325 ($columns[$i + 1] - $columns[$i]) 65 20 Bold -RightToLeft -Alignment Center
    }
    $values = @('1', 'TST-AR-001', 'دواء اختباري أ 500 مجم | صلاحية 2028-09 | تشغيلة SYN-AR-BATCH', '3', 'BOX', '75.00', '5.00', '120.00')
    for ($i = 0; $i -lt $values.Count; $i++) {
        Add-Text $g $values[$i] $columns[$i] 410 ($columns[$i + 1] - $columns[$i]) 95 18 -RightToLeft -Alignment Center
    }

    Add-Text $g 'الإجمالي قبل الخصم: EGP 225.00' 760 640 760 50 30 Bold -RightToLeft -Alignment Far
    Add-Text $g 'الخصم: EGP 11.25' 760 700 760 50 30 -RightToLeft -Alignment Far
    Add-Text $g 'الضريبة: EGP 0.00' 760 760 760 50 30 -RightToLeft -Alignment Far
    Add-Text $g 'الإجمالي: EGP 213.75' 760 830 760 55 36 Bold -RightToLeft -Alignment Far
    Add-Text $g 'جميع الأسماء والأرقام والأسعار والتواريخ مصطنعة بالكامل للاختبار.' 80 1050 1440 45 24 Italic -Color ([System.Drawing.Color]::FromArgb(90, 90, 90)) -RightToLeft -Alignment Center
    return Save-Canvas $canvas 'synthetic-ar-invoice-001.png'
}

function New-MixedInvoicePageOne {
    $canvas = New-Canvas
    $g = $canvas.Graphics
    Add-SyntheticBanner -Graphics $g -Text 'SYNTHETIC / اصطناعي — PAGE 1 OF 2 — NOT A REAL INVOICE'
    Add-Text $g 'Mixed Script Test Supplier / مورد اختبار مختلط' 80 100 1440 70 40 Bold -Alignment Center
    Add-Text $g 'Invoice No: [MISSING]' 80 200 650 45 28 Bold
    Add-Text $g 'التاريخ / Date: 2026-03-10' 870 200 650 45 28 -RightToLeft -Alignment Far
    Add-Text $g 'Currency / العملة: EGP' 80 255 650 45 28

    $columns = @(80, 170, 390, 900, 1030, 1160, 1290, 1420, 1520)
    $rows = @(350, 430, 555)
    Add-TableGrid $g $columns $rows
    $headers = @('#', 'Code', 'الوصف / Description', 'Qty', 'Unit', 'Price', 'D1%', 'Sell')
    for ($i = 0; $i -lt $headers.Count; $i++) {
        Add-Text $g $headers[$i] $columns[$i] 355 ($columns[$i + 1] - $columns[$i]) 65 19 Bold -Alignment Center
    }
    $values = @('1', 'TST-MIX-01', 'دواء TEST-C 250 mg | Exp 2029-03 | Batch MIX-A', '4', 'BOX', '60.00', '0.00', '95.00')
    for ($i = 0; $i -lt $values.Count; $i++) {
        Add-Text $g $values[$i] $columns[$i] 440 ($columns[$i + 1] - $columns[$i]) 105 18 -Alignment Center
    }
    Add-Text $g 'Continue to synthetic page 2 / تابع الصفحة الاصطناعية الثانية' 80 1000 1440 60 30 Bold -Alignment Center
    return Save-Canvas $canvas 'synthetic-mixed-invoice-001-page-1.png'
}

function New-MixedInvoicePageTwo {
    $canvas = New-Canvas
    $g = $canvas.Graphics
    Add-SyntheticBanner -Graphics $g -Text 'SYNTHETIC / اصطناعي — PAGE 2 OF 2 — NOT A REAL INVOICE'
    Add-Text $g 'Mixed Script Test Supplier / مورد اختبار مختلط' 80 100 1440 70 40 Bold -Alignment Center

    $columns = @(80, 170, 390, 900, 1030, 1160, 1290, 1420, 1520)
    $rows = @(260, 340, 465)
    Add-TableGrid $g $columns $rows
    $headers = @('#', 'Code', 'الوصف / Description', 'Qty', 'Unit', 'Price', 'D1%', 'Sell')
    for ($i = 0; $i -lt $headers.Count; $i++) {
        Add-Text $g $headers[$i] $columns[$i] 265 ($columns[$i + 1] - $columns[$i]) 65 19 Bold -Alignment Center
    }
    $values = @('2', 'TST-MIX-02', 'TEST-D شراب 100 ml | Batch MIX-B', '2', 'BOX', '45.00', '5.00', '70.00')
    for ($i = 0; $i -lt $values.Count; $i++) {
        Add-Text $g $values[$i] $columns[$i] 350 ($columns[$i + 1] - $columns[$i]) 105 18 -Alignment Center
    }
    Add-Text $g 'Line 1 Discount 2: 2.00%' 900 545 600 45 26 -Alignment Far
    Add-Text $g 'Line 2 Discount 2: 1.00%' 900 595 600 45 26 -Alignment Far
    Add-Text $g 'Subtotal: EGP 330.00' 900 680 600 45 28 Bold -Alignment Far
    Add-Text $g 'Discount: EGP 10.155' 900 735 600 45 28 -Alignment Far
    Add-Text $g 'Tax: EGP 0.00' 900 790 600 45 28 -Alignment Far
    Add-Text $g 'Total: EGP 319.845' 900 855 600 55 34 Bold -Alignment Far
    Add-Text $g 'Invoice number is deliberately absent to test unresolved/manual review behavior.' 80 1040 1440 45 23 Italic -Color ([System.Drawing.Color]::FromArgb(90, 90, 90)) -Alignment Center
    return Save-Canvas $canvas 'synthetic-mixed-invoice-001-page-2.png'
}

function Get-Sha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-DocumentSha256 {
    param([string[]]$PagePaths)

    $lines = for ($i = 0; $i -lt $PagePaths.Count; $i++) {
        '{0}:{1}' -f ($i + 1), (Get-Sha256 $PagePaths[$i])
    }
    $canonicalPageList = ($lines -join "`n") + "`n"
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($canonicalPageList)
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    return [Convert]::ToHexString($hash).ToLowerInvariant()
}

function New-Evidence {
    param(
        [AllowNull()]$RawValue,
        [AllowNull()]$NormalizedValue,
        [AllowNull()][Nullable[int]]$Page,
        [AllowNull()][object]$Box,
        [string[]]$Warnings = @()
    )

    $evidenceText = if ($null -eq $RawValue) { $null } else { $RawValue }
    return [ordered]@{
        rawValue = $RawValue
        normalizedValue = $NormalizedValue
        page = $Page
        boundingBox = $Box
        evidenceText = $evidenceText
        warnings = @($Warnings)
    }
}

function New-Box {
    param([double]$X, [double]$Y, [double]$Width, [double]$Height)
    return [ordered]@{ x = $X; y = $Y; width = $Width; height = $Height }
}

function New-SourceLine {
    param(
        [string]$Id,
        [int]$Sequence,
        [string]$Description,
        [string]$Code,
        [string]$Quantity,
        [string]$Unit,
        [string]$PurchasePrice,
        [string]$Discount1,
        [string]$Discount2,
        [string]$SellingPrice,
        [AllowNull()]$ExpiryRaw,
        [AllowNull()]$ExpiryDate,
        [string]$Batch,
        [int]$Page
    )

    $rowY = if ($Page -eq 1) { 0.36 + (($Sequence - 1) * 0.09) } else { 0.29 }
    return [ordered]@{
        sourceLineId = $Id
        sequence = $Sequence
        description = New-Evidence $Description $Description $Page (New-Box 0.24 $rowY 0.32 0.07)
        vendorItemCode = New-Evidence $Code $Code $Page (New-Box 0.11 $rowY 0.12 0.07)
        quantity = New-Evidence $Quantity $Quantity $Page (New-Box 0.57 $rowY 0.07 0.07)
        unit = New-Evidence $Unit $Unit $Page (New-Box 0.65 $rowY 0.07 0.07)
        purchaseUnitPrice = New-Evidence $PurchasePrice $PurchasePrice $Page (New-Box 0.73 $rowY 0.07 0.07)
        discount1Percentage = New-Evidence $Discount1 $Discount1 $Page (New-Box 0.81 $rowY 0.07 0.07)
        discount2Percentage = New-Evidence $Discount2 $Discount2 $Page (New-Box 0.81 ([Math]::Min(0.95, $rowY + 0.08)) 0.07 0.04)
        sellingUnitPrice = New-Evidence $SellingPrice $SellingPrice $Page (New-Box 0.89 $rowY 0.07 0.07)
        expiryDate = if ($null -eq $ExpiryDate) {
            New-Evidence $null $null $null $null @('MISSING_EXPIRY')
        } else {
            New-Evidence $ExpiryRaw $ExpiryDate $Page (New-Box 0.25 $rowY 0.15 0.07)
        }
        batch = New-Evidence $Batch $Batch $Page (New-Box 0.40 $rowY 0.14 0.07)
    }
}

function Write-Json {
    param([object]$Value, [string]$Path)
    $json = $Value | ConvertTo-Json -Depth 40
    Set-Content -LiteralPath $Path -Value $json -Encoding utf8NoBOM
}

$englishPage = New-EnglishInvoice
$arabicPage = New-ArabicInvoice
$mixedPageOne = New-MixedInvoicePageOne
$mixedPageTwo = New-MixedInvoicePageTwo

$englishDocumentHash = Get-DocumentSha256 @($englishPage)
$arabicDocumentHash = Get-DocumentSha256 @($arabicPage)
$mixedDocumentHash = Get-DocumentSha256 @($mixedPageOne, $mixedPageTwo)

$provider = [ordered]@{
    name = 'GEMINI'
    model = 'synthetic-fixture-oracle-v1'
    processedAt = '2026-08-21T00:00:00Z'
}

$englishResult = [ordered]@{
    schemaVersion = '1.0'
    resultId = '11111111-1111-4111-8111-111111111111'
    jobId = '11111111-1111-4111-8111-111111111112'
    provider = $provider
    document = [ordered]@{ sourceSha256 = $englishDocumentHash; pageCount = 1; mimeTypes = @('image/png') }
    supplier = New-Evidence 'Synthetic Pharmacy Supplier EN' 'Synthetic Pharmacy Supplier EN' 1 (New-Box 0.05 0.08 0.58 0.07)
    invoiceNumber = New-Evidence 'SYN-EN-0001' 'SYN-EN-0001' 1 (New-Box 0.64 0.09 0.31 0.04)
    invoiceDate = New-Evidence '2026-01-15' '2026-01-15' 1 (New-Box 0.64 0.13 0.31 0.04)
    currency = New-Evidence 'EGP' 'EGP' 1 (New-Box 0.64 0.17 0.31 0.04)
    sourceLines = @(
        (New-SourceLine '11111111-1111-4111-8111-111111111121' 1 'TEST-MED-A 500 mg Capsules' 'TST-001' '2' 'BOX' '100.00' '10.00' '5.00' '150.00' '2028-06' '2028-06-30' 'SYN-BATCH-A' 1),
        (New-SourceLine '11111111-1111-4111-8111-111111111122' 2 'TEST-SYRUP-B 120 ml' 'TST-002' '1' 'BOX' '80.00' '0.00' '0.00' '110.00' '2027-12' '2027-12-31' 'SYN-BATCH-B' 1)
    )
    totals = [ordered]@{
        subtotal = New-Evidence '280.00' '280.00' 1 (New-Box 0.62 0.54 0.32 0.04)
        discount = New-Evidence '29.00' '29.00' 1 (New-Box 0.62 0.59 0.32 0.04)
        tax = New-Evidence '0.00' '0.00' 1 (New-Box 0.62 0.63 0.32 0.04)
        total = New-Evidence '251.00' '251.00' 1 (New-Box 0.62 0.68 0.32 0.05)
    }
    qualityFlags = @()
}

$arabicResult = [ordered]@{
    schemaVersion = '1.0'
    resultId = '22222222-2222-4222-8222-222222222221'
    jobId = '22222222-2222-4222-8222-222222222222'
    provider = $provider
    document = [ordered]@{ sourceSha256 = $arabicDocumentHash; pageCount = 1; mimeTypes = @('image/png') }
    supplier = New-Evidence 'مورد الاختبار الاصطناعي' 'مورد الاختبار الاصطناعي' 1 (New-Box 0.39 0.08 0.56 0.07)
    invoiceNumber = New-Evidence 'SYN-AR-0001' 'SYN-AR-0001' 1 (New-Box 0.05 0.09 0.38 0.04)
    invoiceDate = New-Evidence '20-02-2026' '2026-02-20' 1 (New-Box 0.05 0.13 0.38 0.04)
    currency = New-Evidence 'EGP' 'EGP' 1 (New-Box 0.05 0.17 0.38 0.04)
    sourceLines = @(
        (New-SourceLine '22222222-2222-4222-8222-222222222231' 1 'دواء اختباري أ 500 مجم' 'TST-AR-001' '3' 'BOX' '75.00' '5.00' '0.00' '120.00' '2028-09' '2028-09-30' 'SYN-AR-BATCH' 1)
    )
    totals = [ordered]@{
        subtotal = New-Evidence '225.00' '225.00' 1 (New-Box 0.48 0.53 0.47 0.04)
        discount = New-Evidence '11.25' '11.25' 1 (New-Box 0.48 0.58 0.47 0.04)
        tax = New-Evidence '0.00' '0.00' 1 (New-Box 0.48 0.63 0.47 0.04)
        total = New-Evidence '213.75' '213.75' 1 (New-Box 0.48 0.69 0.47 0.05)
    }
    qualityFlags = @()
}

$mixedResult = [ordered]@{
    schemaVersion = '1.0'
    resultId = '33333333-3333-4333-8333-333333333331'
    jobId = '33333333-3333-4333-8333-333333333332'
    provider = $provider
    document = [ordered]@{ sourceSha256 = $mixedDocumentHash; pageCount = 2; mimeTypes = @('image/png') }
    supplier = New-Evidence 'Mixed Script Test Supplier / مورد اختبار مختلط' 'Mixed Script Test Supplier / مورد اختبار مختلط' 1 (New-Box 0.05 0.08 0.90 0.07)
    invoiceNumber = New-Evidence $null $null $null $null @('MISSING_INVOICE_NUMBER')
    invoiceDate = New-Evidence '2026-03-10' '2026-03-10' 1 (New-Box 0.54 0.17 0.41 0.04)
    currency = New-Evidence 'EGP' 'EGP' 1 (New-Box 0.05 0.21 0.41 0.04)
    sourceLines = @(
        (New-SourceLine '33333333-3333-4333-8333-333333333341' 1 'دواء TEST-C 250 mg' 'TST-MIX-01' '4' 'BOX' '60.00' '0.00' '2.00' '95.00' '2029-03' '2029-03-31' 'MIX-A' 1),
        (New-SourceLine '33333333-3333-4333-8333-333333333342' 2 'TEST-D شراب 100 ml' 'TST-MIX-02' '2' 'BOX' '45.00' '5.00' '1.00' '70.00' $null $null 'MIX-B' 2)
    )
    totals = [ordered]@{
        subtotal = New-Evidence '330.00' '330.00' 2 (New-Box 0.56 0.56 0.38 0.04)
        discount = New-Evidence '10.155' '10.155' 2 (New-Box 0.56 0.61 0.38 0.04)
        tax = New-Evidence '0.00' '0.00' 2 (New-Box 0.56 0.66 0.38 0.04)
        total = New-Evidence '319.845' '319.845' 2 (New-Box 0.56 0.71 0.38 0.05)
    }
    qualityFlags = @('MISSING_INVOICE_NUMBER', 'MIXED_LANGUAGE', 'MANUAL_REVIEW_REQUIRED')
}

Write-Json $englishResult (Join-Path $expectedDirectory 'synthetic-en-invoice-001.ocr-result.v1.json')
Write-Json $arabicResult (Join-Path $expectedDirectory 'synthetic-ar-invoice-001.ocr-result.v1.json')
Write-Json $mixedResult (Join-Path $expectedDirectory 'synthetic-mixed-invoice-001.ocr-result.v1.json')

function New-PageManifest {
    param([int]$Page, [string]$Path)
    return [ordered]@{
        page = $Page
        path = 'sources/' + [System.IO.Path]::GetFileName($Path)
        mimeType = 'image/png'
        sha256 = Get-Sha256 $Path
        widthPixels = $canvasWidth
        heightPixels = $canvasHeight
    }
}

$provenance = [ordered]@{
    kind = 'SYNTHETIC'
    containsRealData = $false
    generator = 'test-data/phase-0/generate-fixtures.ps1'
}

$manifest = [ordered]@{
    schemaVersion = '1.0'
    datasetId = 'PHARMA_AUTO_PHASE_0_SYNTHETIC'
    datasetVersion = '1.0.0'
    classification = 'SYNTHETIC_NO_REAL_BUSINESS_DATA'
    approval = [ordered]@{
        fixturePolicy = 'GENERATED_FROM_SCRATCH'
        approvedBy = 'Repository owner'
        approvedOn = '2026-08-21'
    }
    documents = @(
        [ordered]@{
            documentId = 'SYN-EN-0001'
            languageProfile = 'ENGLISH'
            documentSha256 = $englishDocumentHash
            pages = @((New-PageManifest 1 $englishPage))
            expectedResult = [ordered]@{ path = 'expected/synthetic-en-invoice-001.ocr-result.v1.json'; schemaId = 'https://schemas.pharma-auto.invalid/v1/ocr-result.schema.json' }
            coverage = @('ENGLISH_TEXT', 'EXPIRY_AND_BATCH', 'TWO_DISCOUNTS', 'SELLING_PRICE', 'MULTIPLE_LINES')
            provenance = $provenance
        },
        [ordered]@{
            documentId = 'SYN-AR-0001'
            languageProfile = 'ARABIC'
            documentSha256 = $arabicDocumentHash
            pages = @((New-PageManifest 1 $arabicPage))
            expectedResult = [ordered]@{ path = 'expected/synthetic-ar-invoice-001.ocr-result.v1.json'; schemaId = 'https://schemas.pharma-auto.invalid/v1/ocr-result.schema.json' }
            coverage = @('ARABIC_TEXT', 'EXPIRY_AND_BATCH', 'TWO_DISCOUNTS', 'SELLING_PRICE')
            provenance = $provenance
        },
        [ordered]@{
            documentId = 'SYN-MIXED-0001'
            languageProfile = 'MIXED'
            documentSha256 = $mixedDocumentHash
            pages = @((New-PageManifest 1 $mixedPageOne), (New-PageManifest 2 $mixedPageTwo))
            expectedResult = [ordered]@{ path = 'expected/synthetic-mixed-invoice-001.ocr-result.v1.json'; schemaId = 'https://schemas.pharma-auto.invalid/v1/ocr-result.schema.json' }
            coverage = @('ARABIC_TEXT', 'ENGLISH_TEXT', 'MIXED_SCRIPT', 'MULTI_PAGE', 'EXPIRY_AND_BATCH', 'TWO_DISCOUNTS', 'SELLING_PRICE', 'MISSING_INVOICE_NUMBER', 'MULTIPLE_LINES')
            provenance = $provenance
        }
    )
}

Write-Json $manifest (Join-Path $PSScriptRoot 'manifest.v1.json')
Write-Output "Generated 4 synthetic PNG pages, 3 expected OCR results, and manifest.v1.json."
