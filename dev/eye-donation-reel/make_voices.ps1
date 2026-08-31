$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$dialogueData = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dialogue.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$voiceFolder = Join-Path $PSScriptRoot 'voices'
New-Item -ItemType Directory -Path $voiceFolder -Force | Out-Null
$speech = New-Object System.Speech.Synthesis.SpeechSynthesizer
foreach ($languageCode in @('KO', 'EN')) {
    $voiceName = if ($languageCode -eq 'KO') { 'Microsoft Heami Desktop' } else { 'Microsoft Zira Desktop' }
    $speech.SelectVoice($voiceName)
    foreach ($line in $dialogueData.$languageCode) {
        $speech.Rate = if ($line.role -eq 'zombie') { 1 } else { 2 }
        if ($line.id -eq 'wait') { $speech.Rate = -1 }
        if ($line.id -eq 'cry') { $speech.Rate = -2 }
        $speech.SetOutputToWaveFile((Join-Path $voiceFolder ($languageCode + '_' + $line.id + '.wav')))
        $speech.Speak($line.text)
        $speech.SetOutputToNull()
    }
}
$speech.Dispose()
Write-Output '한글·영어 합성 대사 14개 생성 완료'
