param(
    [Parameter(Mandatory)]
    [int]$AppPid,

    [Parameter(Mandatory)]
    [long]$MainWindowHandle
)

$ErrorActionPreference = 'Stop'

$WinApp = Join-Path $env:APPDATA 'npm\winapp.cmd'
$ArtifactsDir = Join-Path $PSScriptRoot 'ui-test-artifacts'
$ResultsPath = Join-Path $ArtifactsDir 'test-results.json'
$InitialShotPath = Join-Path $ArtifactsDir '01-initial.png'
$AfterEditShotPath = Join-Path $ArtifactsDir '02-after-edit.png'

New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null

$pass = 0
$fail = 0
$results = New-Object System.Collections.Generic.List[object]

function Test-UI {
    param(
        [string]$Name,
        [scriptblock]$Script
    )

    try {
        & $Script
        $script:pass++
        $script:results.Add([pscustomobject]@{
            name = $Name
            status = 'PASS'
        })
    }
    catch {
        $script:fail++
        $script:results.Add([pscustomobject]@{
            name = $Name
            status = 'FAIL'
            detail = $_.Exception.Message
        })
    }
}

function Invoke-WinAppJson {
    param(
        [string[]]$Arguments
    )

    $raw = & $WinApp @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw (($raw | Out-String).Trim())
    }

    if (-not $raw) {
        return $null
    }

    return $raw | ConvertFrom-Json
}

function Flatten-Node {
    param($Node)

    $items = @($Node)
    if ($Node.children) {
        foreach ($child in $Node.children) {
            $items += Flatten-Node $child
        }
    }

    return $items
}

function Get-UiElements {
    $inspect = Invoke-WinAppJson @('ui', 'inspect', '-a', $AppPid, '-d', '20', '--json')
    if (-not $inspect.windows -or $inspect.windows.Count -eq 0) {
        throw 'winapp inspect did not return any windows.'
    }

    return Flatten-Node $inspect.windows[0].elements[0]
}

function Find-Element {
    param(
        [object[]]$Elements,
        [string]$Type,
        [string]$Name
    )

    $match = $Elements | Where-Object {
        $_.type -eq $Type -and $_.name -eq $Name
    } | Select-Object -First 1

    if (-not $match) {
        throw "Element not found: type='$Type', name='$Name'."
    }

    return $match
}

function Set-TextBoxValue {
    param(
        [object]$Element,
        [string]$Text
    )

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName Microsoft.VisualBasic

    Invoke-WinAppJson @('ui', 'focus', $Element.selector, '-a', $AppPid, '--json') | Out-Null
    [Microsoft.VisualBasic.Interaction]::AppActivate($AppPid) | Out-Null
    Start-Sleep -Milliseconds 200

    $current = Invoke-WinAppJson @('ui', 'get-value', $Element.selector, '-a', $AppPid, '--json')
    [System.Windows.Forms.SendKeys]::SendWait('{END}')
    Start-Sleep -Milliseconds 50

    for ($i = 0; $i -lt ($current.text.Length + 5); $i++) {
        [System.Windows.Forms.SendKeys]::SendWait('{BACKSPACE}')
        Start-Sleep -Milliseconds 20
    }

    [System.Windows.Forms.SendKeys]::SendWait($Text)
    Start-Sleep -Milliseconds 400
}

function Assert-Equals {
    param(
        [string]$Actual,
        [string]$Expected,
        [string]$Message
    )

    if ($Actual -ne $Expected) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Assert-Contains {
    param(
        [string]$Actual,
        [string]$ExpectedSubstring,
        [string]$Message
    )

    if ($Actual -notlike "*$ExpectedSubstring*") {
        throw "$Message Expected substring '$ExpectedSubstring', got '$Actual'."
    }
}

$proc = Get-Process -Id $AppPid -ErrorAction SilentlyContinue
if (-not $proc) {
    throw "Process $AppPid is not running."
}

$elements = Get-UiElements

$beamSource = Find-Element $elements 'ComboBox' 'BeamGage source selector'
$ophirSource = Find-Element $elements 'ComboBox' 'Ophir source selector'
$sessionName = Find-Element $elements 'Edit' 'Session name'
$windowSize = Find-Element $elements 'Edit' 'Rolling window size'
$desyncPolicy = Find-Element $elements 'ComboBox' 'Desynchronization policy'
$outputPath = Find-Element $elements 'Edit' 'Output path'
$overviewTab = Find-Element $elements 'TabItem' 'Overview'
$reportTab = Find-Element $elements 'TabItem' 'Report'
$initializeButton = Find-Element $elements 'Button' 'Initialize sources'
$startButton = Find-Element $elements 'Button' 'Start measurement session'
$stopButton = Find-Element $elements 'Button' 'Stop measurement session'
$clearLogButton = Find-Element $elements 'Button' 'Clear event log'

Invoke-WinAppJson @('ui', 'screenshot', '-a', $AppPid, '-o', $InitialShotPath, '--json') | Out-Null

Test-UI 'Main window is alive' {
    $current = Get-Process -Id $AppPid -ErrorAction SilentlyContinue
    if (-not $current -or $current.MainWindowHandle -eq 0) {
        throw 'Main window handle is missing.'
    }
}

Test-UI 'Header title is present' {
    $header = Find-Element (Get-UiElements) 'Text' 'Laser Energy Monitor'
    if (-not $header.selector) {
        throw 'Header title selector is missing.'
    }
}

Test-UI 'BeamGage default source is correct' {
    $value = Invoke-WinAppJson @('ui', 'get-value', $beamSource.selector, '-a', $AppPid, '--json')
    Assert-Equals $value.text 'Simulated BeamGage' 'Unexpected BeamGage source.'
}

Test-UI 'Ophir default source is correct' {
    $value = Invoke-WinAppJson @('ui', 'get-value', $ophirSource.selector, '-a', $AppPid, '--json')
    Assert-Equals $value.text 'Simulated Ophir' 'Unexpected Ophir source.'
}

Test-UI 'Session name default value is correct' {
    $value = Invoke-WinAppJson @('ui', 'get-value', $sessionName.selector, '-a', $AppPid, '--json')
    Assert-Equals $value.text 'Prototype Session' 'Unexpected session name.'
}

Test-UI 'Window N default value is correct' {
    $value = Invoke-WinAppJson @('ui', 'get-value', $windowSize.selector, '-a', $AppPid, '--json')
    Assert-Equals $value.text '20,0' 'Unexpected Window N value.'
}

Test-UI 'Desynchronization policy default value is correct' {
    $value = Invoke-WinAppJson @('ui', 'get-value', $desyncPolicy.selector, '-a', $AppPid, '--json')
    Assert-Equals $value.text 'Fault Session' 'Unexpected desynchronization policy.'
}

Test-UI 'Output path points to workbook' {
    $value = Invoke-WinAppJson @('ui', 'get-value', $outputPath.selector, '-a', $AppPid, '--json')
    Assert-Contains $value.text 'measurement-session.xlsx' 'Unexpected output path.'
}

Test-UI 'Primary action buttons are enabled' {
    foreach ($button in @($initializeButton, $startButton, $clearLogButton)) {
        $state = Invoke-WinAppJson @('ui', 'get-property', $button.selector, '-a', $AppPid, '-p', 'IsEnabled', '--json')
        Assert-Equals $state.properties.IsEnabled 'True' "Button '$($button.name)' is not enabled."
    }

    $stopState = Invoke-WinAppJson @('ui', 'get-property', $stopButton.selector, '-a', $AppPid, '-p', 'IsEnabled', '--json')
    Assert-Equals $stopState.properties.IsEnabled 'False' "Button '$($stopButton.name)' should be disabled in Idle state."
}

Test-UI 'Overview and Report tabs exist' {
    foreach ($tab in @($overviewTab, $reportTab)) {
        if (-not $tab.selector) {
            throw "Tab '$($tab.name)' selector is missing."
        }
    }
}

Test-UI 'Session Name can be edited and restored' {
    Set-TextBoxValue $sessionName 'UI Test Session'
    $updated = Invoke-WinAppJson @('ui', 'get-value', $sessionName.selector, '-a', $AppPid, '--json')
    Assert-Contains $updated.text 'UI Test Session' 'Session Name was not updated.'

    Set-TextBoxValue $sessionName 'Prototype Session'
    $restored = Invoke-WinAppJson @('ui', 'get-value', $sessionName.selector, '-a', $AppPid, '--json')
    Assert-Contains $restored.text 'Prototype Session' 'Session Name was not restored.'

    Invoke-WinAppJson @('ui', 'screenshot', '-a', $AppPid, '-o', $AfterEditShotPath, '--json') | Out-Null
}

Test-UI 'Events list area is visible in the automation tree' {
    $currentElements = Get-UiElements
    $eventList = $currentElements | Where-Object {
        $_.automationId -eq 'EventsList' -or
        $_.selector -eq 'EventsList' -or
        $_.type -in @('List', 'ListItem') -or
        $_.className -match 'LISTBOX'
    } | Select-Object -First 1

    if (-not $eventList) {
        throw 'No visible List/ListItem/LISTBOX element was found for the Events section.'
    }
}

Test-UI 'Interactive controls have AutomationId coverage' {
    $currentElements = Get-UiElements
    $interactive = $currentElements | Where-Object {
        $_.type -in @('Button', 'Edit', 'ComboBox')
    } | Where-Object {
        -not (
            ($_.type -eq 'Button' -and [string]::IsNullOrWhiteSpace($_.automationId)) -or
            $_.automationId -in @('UpButton', 'DownButton', 'DownPageButton')
        )
    }

    $missing = $interactive | Where-Object { [string]::IsNullOrWhiteSpace($_.automationId) }
    if ($missing.Count -gt 0) {
        $names = ($missing | ForEach-Object { "$($_.type) '$($_.name)'" }) -join ', '
        throw "Missing AutomationId: $names"
    }
}

$results | ConvertTo-Json -Depth 5 | Set-Content -Path $ResultsPath -Encoding UTF8

Write-Host "`nPassed: $pass | Failed: $fail"
if ($fail -gt 0) {
    $results | Where-Object { $_.status -eq 'FAIL' } | ForEach-Object {
        Write-Host "FAIL: $($_.name) - $($_.detail)" -ForegroundColor Red
    }
    exit 1
}

exit 0
