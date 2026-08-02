# Deterministic control-vector calibration sweep.
# Runs each (trait, strength) below through llama-cli with --temp 0 (repeatable),
# one fresh process each (no carry-over between runs). Edit the $tests list to taste.
#
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File .\test-vectors.ps1

Set-Location $PSScriptRoot   # the ml folder

$llama  = ".\llama.cpp\llama-cli.exe"   # llama-cli templates the chat prompt correctly; -st makes it single-turn
$model  = ".\models\Hermes-3-Llama-3.1-8B-Q4_K_M.gguf"
$sys    = "You are Corin Maret, a warm northern innkeeper. First person, in character."
$prompt = "Evening. Busy day?"

# trait, strength  — add rows to sweep a trait at several strengths
# FINAL calibrated set (base hermes). fear omitted — doesn't isolate cleanly; fine-tune covers it.
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
  $vec  = ".\control-vectors\out\$name.gguf:$scale"   # relative path → only colon is the scale
  Write-Host "`n========== $name @ $scale ==========" -ForegroundColor Cyan
  # -st = single turn then exit (templates the prompt properly, no interactive hang).
  # $null on stdin is belt-and-suspenders. There'll be timing-stat lines after each reply —
  # just read the prose under the header.
  $null | & $llama -m $model -ngl 99 --temp 0 -st --control-vector-scaled $vec -sys $sys -p $prompt -n 80
}

Write-Host "`n========== done ==========" -ForegroundColor Green
