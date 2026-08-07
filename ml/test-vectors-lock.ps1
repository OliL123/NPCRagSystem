# Final lock sweep for the FINE-TUNE (hermes-npc), 2026-08.
# anger/suspicion/grief/disgust already locked. exhaustion + hope dropped (don't isolate).
# This pins the last two borderlines: anxiety (~0.5 window) and guilt (~0.8).
# Deterministic (--temp 0).  Run:  powershell -ExecutionPolicy Bypass -File .\test-vectors-lock.ps1
Set-Location $PSScriptRoot

$llama  = ".\llama.cpp\llama-cli.exe"
$model  = ".\finetune\hermes-npc.Q4_K_M.gguf"
$sys    = "You are Corin Maret, a warm northern innkeeper. First person, in character."
$prompt = "Evening. Busy day?"

if (-not (Test-Path $model)) { Write-Host "Model not found: $model" -ForegroundColor Red; return }
if (-not (Test-Path $llama)) { Write-Host "llama-cli not found: $llama" -ForegroundColor Red; return }

$tests = @(
  @("anxiety", 0.5),
  @("anxiety", 0.55),
  @("guilt",   0.8)
)

foreach ($t in $tests) {
  $name = $t[0]; $scale = $t[1]
  $vec  = ".\control-vectors\out\$name.gguf:$scale"
  Write-Host "`n========== $name @ $scale ==========" -ForegroundColor Cyan
  $null | & $llama -m $model -ngl 99 --temp 0 -st --control-vector-scaled $vec -sys $sys -p $prompt -n 80
}

Write-Host "`n========== done. want: nervous-but-coherent anxiety, and guilt that deflects/unsettles ==========" -ForegroundColor Green
