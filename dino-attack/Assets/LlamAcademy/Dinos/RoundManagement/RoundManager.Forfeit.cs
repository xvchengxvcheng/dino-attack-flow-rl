namespace LlamAcademy.Dinos.RoundManagement
{
    public partial class RoundManager
    {
        public void ForfeitSession()
        {
            if (State == GameState.Running)
            {
                BeginEnding(false);
            }
        }

        public void ForfeitDefenderSession()
        {
            if (State == GameState.Running)
            {
                BeginEnding(true);
            }
        }
    }
}
