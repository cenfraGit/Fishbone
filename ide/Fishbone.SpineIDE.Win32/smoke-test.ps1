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
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  public struct RECT { public int l, t, r, b; }
  public static string Text(IntPtr h) { var s = new StringBuilder(200000); SendMessageW(h, 0x0D, (IntPtr)200000, s); return s.ToString(); }
}
'@
# debugging saves the script first, so the test works on a file of its own
$script = Join-Path $Out "smoke.fb"
Set-Content -Path $script -Encoding utf8 -Value @'
// F5 debug / continue    Ctrl+F5 run    Shift+F5 stop
// F9 breakpoint    F10 step over    F11 step into    Shift+F11 step out
// Ctrl+Space completion
func square(x)
{
    return x * x;
}

let i = 0;
while (i < 10000)
{
    let squared = square(i);
    println("line " + i.ToString() + " squared is " + squared.ToString());
    i = i + 1;
}
'@
$p = Start-Process $Exe -ArgumentList "`"$script`"" -PassThru
Start-Sleep -Seconds 2; $p.Refresh(); $w = $p.MainWindowHandle
$ed = [W]::FindWindowExW($w, [IntPtr]::Zero, "Scintilla", $null)
$st = [W]::FindWindowExW($w, [IntPtr]::Zero, "msctls_statusbar32", $null)
function Key($vk) { [W]::PostMessageW($ed, 0x100, [IntPtr]$vk, [IntPtr]::Zero) | Out-Null; [W]::PostMessageW($ed, 0x101, [IntPtr]$vk, [IntPtr]::Zero) | Out-Null }
function Send-Text($s) { foreach ($c in $s.ToCharArray()) { if ($c -eq "`n") { Key 0x0D } else { [W]::PostMessageW($ed, 0x102, [IntPtr][int]$c, [IntPtr]::Zero) | Out-Null }; Start-Sleep -Milliseconds 30 } }
# PrintWindow draws the window itself, so the shot works even when another window is in front
function Shot($name) {
  $r = New-Object W+RECT; [W]::GetWindowRect($w, [ref]$r) | Out-Null
  $bmp = New-Object System.Drawing.Bitmap ($r.r - $r.l), ($r.b - $r.t)
  $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
  [W]::PrintWindow($w, $dc, 2) | Out-Null   # PW_RENDERFULLCONTENT
  $g.ReleaseHdc($dc); $g.Dispose()
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

