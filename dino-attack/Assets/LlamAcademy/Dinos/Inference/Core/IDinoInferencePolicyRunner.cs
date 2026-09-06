using LlamAcademy.Dinos.Training;

namespace LlamAcademy.Dinos.Inference
{
    public interface IDinoInferencePolicyRunner
    {
        bool TryRun(
            DinoStructuredObservationFrame frame,
            out float[] actions,
            out string failureReason);
    }
}
