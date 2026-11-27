namespace VoltTray.UI.Tokens;

public static class LocalizationConstants
{
    public const string SpanishCulturePrefix = "es";
    public const string UnavailablePlaceholder = "--";

    public const string RegistryPersonalizePath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public const string AppsUseLightThemeValue = "AppsUseLightTheme";
    public const string SystemUsesLightThemeValue = "SystemUsesLightTheme";

    public const int VkTab = 0x09;
    public const int VkReturn = 0x0D;
    public const int VkEscape = 0x1B;
    public const int VkSpace = 0x20;

    public const string StatusOnlineEn = "Online";
    public const string StatusOnlineEs = "En línea";
    public const string StatusOnBatteryEn = "On battery";
    public const string StatusOnBatteryEs = "En batería";
    public const string StatusAvrEn = "AVR Active";
    public const string StatusAvrEs = "Regulación AVR";
    public const string StatusFaultEn = "UPS Fault";
    public const string StatusFaultEs = "Falla del UPS";
    public const string StatusNoCommEn = "No communication";
    public const string StatusNoCommEs = "Sin comunicación";
    public const string StatusPausedEn = "Paused";
    public const string StatusPausedEs = "Pausado";

    public const string GridSectionEn = "Grid & Inverter";
    public const string GridSectionEs = "Red e Inversor";
    public const string BatterySectionEn = "Battery";
    public const string BatterySectionEs = "Batería";
    public const string ActiveLoadSectionEn = "Active Load";
    public const string ActiveLoadSectionEs = "Carga Activa";

    public const string InputLabelEn = "Input";
    public const string InputLabelEs = "Entrada";
    public const string OutputLabelEn = "Output";
    public const string OutputLabelEs = "Salida";
    public const string TemperatureLabelEn = "Temp";
    public const string TemperatureLabelEs = "Temp";
    public const string RuntimeLabelEn = "Runtime";
    public const string RuntimeLabelEs = "Autonomía";
    public const string LoadCapacitySuffixEn = "load";
    public const string LoadCapacitySuffixEs = "carga";

    public const string BatteryTestEn = "Battery test";
    public const string BatteryTestEs = "Prueba de batería";
    public const string BatteryTestRunningEn = "Test in progress";
    public const string BatteryTestRunningEs = "Prueba en curso";
    public const string MuteAlarmEn = "Mute alarm";
    public const string MuteAlarmEs = "Silenciar alarma";
    public const string UnmuteAlarmEn = "Unmute alarm";
    public const string UnmuteAlarmEs = "Activar alarma";

    public const string ExitTitleEn = "Close VoltTray?";
    public const string ExitTitleEs = "¿Cerrar VoltTray?";
    public const string ExitBodyEn = "UPS monitoring will stop. Your PC will not shut down or hibernate automatically during a prolonged power outage.";
    public const string ExitBodyEs = "El monitoreo del UPS se detendrá. Tu PC no se apagará ni hibernará automáticamente si ocurre un corte de energía prolongado.";
    public const string ExitPauseLabelEn = "Pause monitoring";
    public const string ExitPauseLabelEs = "Pausar monitoreo";
    public const string ExitPauseHelpEn = "releases the USB port and keeps VoltTray in the tray.";
    public const string ExitPauseHelpEs = "libera el puerto USB y deja VoltTray en la bandeja.";
    public const string ExitCloseLabelEn = "Close completely";
    public const string ExitCloseLabelEs = "Cerrar por completo";
    public const string ExitCloseHelpEn = "exits VoltTray and frees its memory.";
    public const string ExitCloseHelpEs = "cierra VoltTray y libera la memoria.";
    public const string ExitCancelEn = "Cancel";
    public const string ExitCancelEs = "Cancelar";
}
