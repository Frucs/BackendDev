namespace Kajens_Cykeluthyrning
{
    public class ElectricBicycle : Vehicle
    {
        public int BatteryLevel;

        public ElectricBicycle(int number, decimal hourlyRate, bool isAvailable, int batteryLevel) : base(number, hourlyRate, isAvailable)
        {
            BatteryLevel = batteryLevel;
        }

        public void ConsumeBattery(int hours)
        {
            BatteryLevel -= hours * 10;

            if (BatteryLevel < 0)
            {
                BatteryLevel = 0;
            }
        }

        public void Charge()
        {
            BatteryLevel = 100;
        }
    }
}
