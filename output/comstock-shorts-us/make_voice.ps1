param(
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$voice = New-Object System.Speech.Synthesis.SpeechSynthesizer
$voice.SelectVoice('Microsoft Zira Desktop')
$voice.Rate = 5
$voice.Volume = 100
$voice.SetOutputToWaveFile($OutputPath)
$voice.Speak('America! Got a zombie problem? Add more guns! Comstock gives you one hundred thirty-four parts, fourteen weapons, and one deeply unreasonable robot. It auto-fires, because aiming is a meeting. Survive twenty waves. Fight the boss. Build worse. Win better. Comstock. Play now!')
$voice.Dispose()
