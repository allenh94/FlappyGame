using System.Runtime.CompilerServices;

// 供 EditMode 測試（Assembly-CSharp-Editor）存取 internal 測試 seam（internal 建構注入，比照 ScoreSession.cs:60-63）。
// 專案慣例不引入 asmdef（bg-layer-editor D6），故以 InternalsVisibleTo 暴露 internal seam——
// 僅 Editor 組件可見，不影響 runtime 封裝（LB4 持久化隔離維持）。
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]