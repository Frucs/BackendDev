namespace Kajens_Cykeluthyrning
{
    public class Rental
    {
        public Customer Customer;
        public Vehicle Vehicle;
        public int Hours;

        public Rental(Customer customer, Vehicle vehicle, int hours)
        {
            Customer = customer;
            Vehicle = vehicle;
            Hours = hours;
        }

        public decimal CalculatePrice()
        {
            decimal price = Vehicle.CalculatePrice(Hours);

            if (Customer.IsMember)
            {
                price *= 0.90m;
            }

            return price;
        }
    }
}