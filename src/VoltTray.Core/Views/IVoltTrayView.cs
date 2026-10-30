using VoltTray.Core.Models;

namespace VoltTray.Core.Views;

public interface IVoltTrayView : IDisposable
{
    void Initialize();
    void UpdateTelemetry(UpsTelemetry telemetry);
    void SetOperationalState(UpsOperationalState state);
}
