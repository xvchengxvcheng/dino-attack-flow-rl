namespace LlamAcademy.Dinos.Map
{
    public interface IBattlefieldLayoutSignatureProvider
    {
        bool TryCapture(out BattlefieldLayoutSignature signature);
    }
}
