var windows = UnityEngine.Resources.FindObjectsOfTypeAll<LiteGame.Editor.UiTools.Workbench.UiWorkbenchWindow>();
var w = windows[0];
var field = w.GetType().GetField("_state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var state = field.GetValue(w);
var prop = state.GetType().GetProperty("Perspective");
return w.title + "|" + prop.GetValue(state) + "|" + LiteGame.Editor.UiTools.Workbench.UiWorkbenchPerspectives.All.Count;
