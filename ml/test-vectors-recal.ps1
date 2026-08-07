# Re-calibration sweep for the FINE-TUNE (hermes-npc), 2026-08.
# The base-hermes vectors transfer to the fine-tune, but four traits need re-tuning because
# the LoRA moved their activation geometry:
#   anxiety   - 0.6 was too hot (degraded into "uhh uhh" loops) -> try lower
#   guilt     - 0.7 was weak -> try higher
#   exhaustion- -0.45 was DEAD on the fine-tune -> sweep both signs to find the tired direction
#   hope      - -0.55 produced HOPE not despair (sign flipped) -> sweep both signs
# anger/suspicion/grief/disgust already work at base strengths, so they're omitted here.
# Deterministic (--temp 0). Compare each against the baseline in test-vectors-finetune.ps1.
#
# Run:  powershell -ExecutionPolicy Bypass -File .\test-vectors-recal.ps1
Set-Location $PSScriptRoot

$llama  = ".\llama.cpp\llama-cli.exe"
$model  = ".\finetune\hermes-npc.Q4_K_M.gguf"
$sys    = "You are Corin Maret, a warm northern innkeeper. First person, in character."
$prompt = "Evening. Busy day?"

if (-not (Test-Path $model)) { Write-Host "Model not found: $model" -ForegroundColor Red; return }
if (-not (Test-Path $llama)) { Write-Host "llama-cli not found: $llama" -ForegroundColor Red; return }

$tests = @(
  @("anxiety",    0.3),
  @("anxiety",    0.45),
  @("guilt",      0.9),
  @("exhaustion", 0.45),
  @("exhaustion", 0.7),
  @("exhaustion", -0.7),
  @("hope",       0.5),
  @("hope",       -0.8)
)

foreach ($t in $tests) {
  $name = $t[0]; $scale = $t[1]
  $vec  = ".\control-vectors\out\$name.gguf:$scale"
  Write-Host "`n========== $name @ $scale ==========" -ForegroundColor Cyan
  $null | & $llama -m $model -ngl 99 --temp 0 -st --control-vector-scaled $vec -sys $sys -p $prompt -n 80
}

Write-Host "`n========== done. anxious-but-coherent / tired / bleak are the targets ==========" -ForegroundColor Green
