/// <summary>
/// Глобальный флаг открытого кастомного меню (Esc). Вынесен в обычный .cs
/// чтобы другие компоненты могли его читать без зависимости от того,
/// успешно ли скомпилировался .razor (бывают каскадные сбои razor-генерации).
/// </summary>
public static class MenuState
{
	public static bool SettingsOpen { get; set; } = false;
}
