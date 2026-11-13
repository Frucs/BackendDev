namespace Kajens_Cykeluthyrning
{
    public class Vehicle
    {
        public int Number;
        public decimal HourlyRate;
        public bool IsAvailable;

        public Vehicle(int number, decimal hourlyRate, bool isAvailable)
        {
            Number = number;
            HourlyRate = hourlyRate;
            IsAvailable = isAvailable;
        }

        public decimal CalculatePrice(int hours)
        {
            return HourlyRate * hours;
        }
    }
}
