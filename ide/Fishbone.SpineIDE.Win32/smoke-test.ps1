# drives a running spineide.exe with posted keys and saves screenshots, as a quick smoke test:
#   powershell -ExecutionPolicy Bypass -File smoke-test.ps1 -Exe <path to spineide.exe> -Out <folder>
# it sets a breakpoint, debugs, steps, edits and folds. the window takes focus while it runs.
param([string]$Exe, [string]$Out)
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class W {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowExW(IntPtr p, IntPtr a, string c, string n);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, StringBuilder l);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public struct RECT { public int l, t, r, b; }
  public static string Text(IntPtr h) { var s = new StringBuilder(200000); SendMessageW(h, 0x0D, (IntPtr)200000, s); return s.ToString(); }
}
'@
$p = Start-Process $Exe -PassThru
Start-Sleep -Seconds 2; $p.Refresh(); $w = $p.MainWindowHandle
$ed = [W]::FindWindowExW($w, [IntPtr]::Zero, "Scintilla", $null)
$outBox = [W]::FindWindowExW($w, [IntPtr]::Zero, "Edit", $null)
$vars = [W]::FindWindowExW($w, $outBox, "Edit", $null)
$st = [W]::FindWindowExW($w, [IntPtr]::Zero, "Static", $null)
function Key($vk) { [W]::PostMessageW($ed, 0x100, [IntPtr]$vk, [IntPtr]::Zero) | Out-Null; [W]::PostMessageW($ed, 0x101, [IntPtr]$vk, [IntPtr]::Zero) | Out-Null }
function Send-Text($s) { foreach ($c in $s.ToCharArray()) { if ($c -eq "`n") { Key 0x0D } else { [W]::PostMessageW($ed, 0x102, [IntPtr][int]$c, [IntPtr]::Zero) | Out-Null }; Start-Sleep -Milliseconds 30 } }
function Shot($name) {
  [W]::SetForegroundWindow($w) | Out-Null; Start-Sleep -Milliseconds 300
  $r = New-Object W+RECT; [W]::GetWindowRect($w, [ref]$r) | Out-Null
  $bmp = New-Object System.Drawing.Bitmap ($r.r - $r.l), ($r.b - $r.t)
  $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.l, $r.t, 0, 0, $bmp.Size); $g.Dispose()
  $bmp.Save("$Out\$name.png")
}
function Status { $p.Refresh(); "  status: " + [W]::Text($st) + " | title: " + $p.MainWindowTitle }

# breakpoint on the "let squared" line (0-based 11), then debug
[W]::SendMessageW($ed, 2024, [IntPtr]11, [IntPtr]::Zero) | Out-Null   # SCI_GOTOLINE
Key 0x78; Start-Sleep -Milliseconds 300                               # F9
Key 0x74; Start-Sleep -Seconds 4                                      # F5
"after F5:"; Status; Shot "1_paused"
Key 0x79; Start-Sleep -Seconds 1                                      # F10
"after F10:"; Status
Key 0x7A; Start-Sleep -Seconds 1                                      # F11 into square()
"after F11:"; Status; Shot "2_stepped_in"
Key 0x74; Start-Sleep -Seconds 1                                      # F5 continue, hits breakpoint again
"after continue:"; Status
"variables pane:"; ([W]::Text($vars) -split "`n" | Select-Object -First 12) | ForEach-Object { "  $_" }
# shift+F5 stop: fake the shift state is not possible from here, so remove breakpoint and continue instead
[W]::SendMessageW($ed, 2024, [IntPtr]11, [IntPtr]::Zero) | Out-Null
Key 0x78; Start-Sleep -Milliseconds 300
Key 0x74; Start-Sleep -Seconds 6
"after removing breakpoint + continue:"; Status

# indentation + completion: append a block at the end
[W]::SendMessageW($ed, 2318, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null  # SCI_DOCUMENTEND
Send-Text "`nif (i > 0)`n{`nlet x = 1;`n}"
Send-Text "`nsq"
Start-Sleep -Milliseconds 500
Shot "3_typed"
Key 0x1B
$text = [W]::Text($ed)
"tail of editor text:"; ($text -split "`n" | Select-Object -Last 7) | ForEach-Object { "  [$_]" }

# fold the square() function by clicking isn't possible, but ctrl-free fold toggle: SCI_TOGGLEFOLD line 3
[W]::SendMessageW($ed, 2231, [IntPtr]3, [IntPtr]::Zero) | Out-Null
Start-Sleep -Milliseconds 300
Shot "4_folded"
$p | Stop-Process

