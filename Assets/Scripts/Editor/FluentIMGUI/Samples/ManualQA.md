# Fluent IMGUI manual QA

Automated Editor tests cover lazy builder configuration, range validation and clamping,
scope disposal, disabled-state restoration, style isolation, and assembly boundaries.
Window resizing and domain-reload behavior remain manual because reliable automation would
require controlling Unity's native host-window lifecycle rather than the DSL itself.

1. Open **Window > MF2 > Fluent IMGUI Demo**.
2. Resize the window very small and very large. Confirm padding and the eight-pixel gaps remain consistent and no layout mismatch appears in Console.
3. Enter values below `0` and above `100` in **Speed**. Confirm the displayed/returned value clamps immediately once Unity accepts the typed value.
4. Click **Save** and **Reset**. Confirm Save logs the current speed and Reset restores `5`.
5. Click **Disable Save**. Confirm Save becomes disabled, then click **Enable Save** and confirm it becomes interactive again.
6. Repaint repeatedly by resizing or obscuring/revealing the window. Trigger a script/domain reload, reopen the window, and confirm Console has no GUILayout mismatch warnings.
7. Repeat the checklist on Windows with Unity 6.3 when that Editor version is available.
