# Control-vector test on the FINE-TUNED model (hermes-npc), 2026-08.
# Same idea as test-vectors.ps1, but pointed at the fine-tune GGUF instead of base hermes.
# Purpose: check whether the base-extracted vectors still STEER the fine-tune (they may
# transfer, since the fine-tune is base + a small LoRA delta). Runs a no-vector BASELINE
# first so you can see exactly what each vector adds. Deterministic (--temp 0).
#
# Run:  powershell -ExecutionPolicy Bypass -File .\test-vectors-finetune.ps1
Set-Location $PSScriptRoot   # the ml folder

$llama  = ".\llama.cpp\llama-cli.exe"
$model  = ".\finetune\hermes-npc.Q4_K_M.gguf"   # the fine-tune built this session
$sys    = "You are Corin Maret, a warm northern innkeeper. First person, in character."
$prompt = "Evening. Busy day?"

if (-not (Test-Path $model)) {
  Write-Host "Model not found: $model  (fix the path to your fine-tune GGUF)" -ForegroundColor Red
  return
}
if (-not (Test-Path $llama)) {
  Write-Host "llama-cli not found: $llama" -ForegroundColor Red
  return
}

# Baseline: fine-tune with NO vector, so the vector runs below have something to compare to.
Write-Host "`n========== BASELINE  (fine-tune, no vector) ==========" -ForegroundColor Yellow
$null | & $llama -m $model -ngl 99 --temp 0 -st -sys $sys -p $prompt -n 80

# The base-hermes calibrated strengths. If a vector steers the fine-tune here, Track B
# works with the EXISTING vectors (no re-extraction). If it does nothing or garbles the
# output, that trait needs re-extraction on the fine-tuned weights.
$tests = @(
  @("anger",      0.75),
  @("anxiety",    0.6),
  @("suspicion",  0.75),
  @("disgust",    0.7),
  @("guilt",      0.7),
  @("exhaustion", -0.45),
  @("grief",      0.8),
  @("hope",       -0.55)
)

foreach ($t in $tests) {
  $name = $t[0]; $scale = $t[1]
  $vec  = ".\control-vectors\out\$name.gguf:$scale"   # relative path, only the colon is the scale
  Write-Host "`n========== $name @ $scale ==========" -ForegroundColor Cyan
  $null | & $llama -m $model -ngl 99 --temp 0 -st --control-vector-scaled $vec -sys $sys -p $prompt -n 80
}

Write-Host "`n========== done. compare each trait against the BASELINE at the top ==========" -ForegroundColor Green
