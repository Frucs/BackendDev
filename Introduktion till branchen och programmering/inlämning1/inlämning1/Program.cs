using Kajens_Cykeluthyrning;
using Spectre.Console;

/*
* Jag använder Spectre's MarkupLine istället för Console.WriteLine
* för att det är lite snyggare i konsolappen. */

AnsiConsole.MarkupLine("[green]=== Kajens Cykeluthyrning ===[/]");

// En kundlista med alla kunder.
List<Customer> customerList = new List<Customer>();

// En fordonslista med alla fordon.
List<Vehicle> vehicleList = new List<Vehicle>();

// Skapa startfordon.
vehicleList.Add(new Bicycle(1, 40, true));
vehicleList.Add(new Bicycle(2, 40, true));
vehicleList.Add(new ElectricBicycle(3, 70, true, 80));
vehicleList.Add(new ElectricBicycle(4, 70, true, 25));

// Lista med aktiva uthyrningar.
List<Rental> rentalList = new List<Rental>();

string read = "";

while (read != "exit" && read != "6")
{
    Console.WriteLine("\n1. Visa fordon  2. Registrera kund  3. Hyr ut");
    Console.WriteLine("4. Lämna tillbaka  5. Ladda  6. Avsluta\n");

    Console.Write("Val: ");
    read = Console.ReadLine();

    if (read == "1")
    {
        foreach (Vehicle vehicle in vehicleList)
        {
            string status = vehicle.IsAvailable ? "Ledig" : "Uthyrd";

            if (vehicle is ElectricBicycle)
            {
                ElectricBicycle electricBicycle = (ElectricBicycle)vehicle;

                Console.WriteLine(
                    $"{vehicle.Number} Elcykel {status} {electricBicycle.BatteryLevel} %");
            }
            else
            {
                Console.WriteLine(
                    $"{vehicle.Number} Cykel {status}");
            }
        }
    }

    if (read == "2")
    {
        string name = "";
        int age = -1;
        bool isMember = false;

        Console.Write("Namn: ");
        name = Console.ReadLine();

        Console.Write("Ålder: ");
        age = SafeInteger();

        Console.Write("Medlem (j/n): ");
        read = Console.ReadLine();

        if (read == "j")
        {
            isMember = true;
        }

        customerList.Add(new Customer(name, age, isMember));

        Console.WriteLine(name + " är registrerad.");
    }

    if (read == "3")
    {
        Console.Write("Kund: ");
        string customerName = Console.ReadLine();

        Customer customer = customerList.Find(c => c.Name == customerName);

        if (customer == null)
        {
            Console.WriteLine("Kunden finns inte.");
        }
        else
        {
            Console.Write("Fordonsnummer: ");
            int vehicleNumber = SafeInteger();

            Vehicle vehicle = vehicleList.Find(v => v.Number == vehicleNumber);

            if (vehicle == null)
            {
                Console.WriteLine("Fordonet finns inte.");
            }
            else if (!vehicle.IsAvailable)
            {
                Console.WriteLine("Fordonet är redan uthyrt.");
            }
            else if (vehicle is Bicycle && !(vehicle is ElectricBicycle) && customer.Age < 15)
            {
                Console.WriteLine("Kunden måste vara minst 15 år för att hyra en cykel.");
            }
            else if (vehicle is ElectricBicycle && customer.Age < 18)
            {
                Console.WriteLine("Kunden måste vara minst 18 år för att hyra en elcykel.");
            }
            else if (vehicle is ElectricBicycle && ((ElectricBicycle)vehicle).BatteryLevel < 30)
            {
                ElectricBicycle electricBicycle = (ElectricBicycle)vehicle;

                Console.WriteLine($"Elcykel {vehicle.Number} har för lite batteri ({electricBicycle.BatteryLevel} %). Ladda den först.");
            }
            else
            {
                Console.Write("Antal timmar: ");
                int hours = SafeInteger();

                if (hours <= 0)
                {
                    Console.WriteLine("Antalet timmar måste vara minst 1.");
                }
                else
                {
                    Rental rental = new Rental(customer, vehicle, hours);

                    rentalList.Add(rental);
                    vehicle.IsAvailable = false;

                    Console.WriteLine($"{customer.Name} hyr {(vehicle is ElectricBicycle ? "Elcykel" : "Cykel")} {vehicle.Number} i {hours} timmar.");
                }
            }
        }
    }

    if (read == "4")
    {
        Console.Write("Fordonsnummer: ");
        int vehicleNumber = SafeInteger();

        Rental rental = rentalList.Find(r => r.Vehicle.Number == vehicleNumber);

        if (rental == null)
        {
            Console.WriteLine("Ingen aktiv uthyrning hittades för detta fordon.");
        }
        else
        {
            decimal price = rental.CalculatePrice();
            decimal originalPrice = rental.Vehicle.CalculatePrice(rental.Hours);
            decimal discount = originalPrice - price;

            // Förbruka batteri om fordonet är en elcykel.
            if (rental.Vehicle is ElectricBicycle)
            {
                ElectricBicycle electricBicycle = (ElectricBicycle)rental.Vehicle;
                electricBicycle.ConsumeBattery(rental.Hours);
            }

            // Markera fordonet som ledigt.
            rental.Vehicle.IsAvailable = true;

            // Ta bort uthyrningen från listan.
            rentalList.Remove(rental);

            // Skriv ut kvitto.
            Console.WriteLine("\n========== KVITTO ==========");
            Console.WriteLine($"Kund: {rental.Customer.Name}");
            Console.WriteLine($"Fordon: {(rental.Vehicle is ElectricBicycle ? "Elcykel" : "Cykel")} {rental.Vehicle.Number}");
            Console.WriteLine($"Antal timmar: {rental.Hours}");
            Console.WriteLine($"Ordinarie pris: {originalPrice:F2} kr");

            if (rental.Customer.IsMember)
            {
                Console.WriteLine($"Medlemsrabatt (10 %): -{discount:F2} kr");
            }

            Console.WriteLine($"Att betala: {price:F2} kr");
            Console.WriteLine("============================\n");
        }
    }


    if (read == "5")
    {
        Console.Write("Fordonsnummer: ");
        int vehicleNumber = SafeInteger();

        Vehicle vehicle = vehicleList.Find(v => v.Number == vehicleNumber);

        if (vehicle == null)
        {
            Console.WriteLine("Fordonet finns inte.");
        }
        else if (vehicle is ElectricBicycle)
        {
            ElectricBicycle electricBicycle = (ElectricBicycle)vehicle;

            if (!electricBicycle.IsAvailable)
            {
                Console.WriteLine("Du kan inte ladda en elcykel som är uthyrd.");
            }
            else
            {
                electricBicycle.Charge();

                Console.WriteLine($"Elcykel {vehicle.Number} är nu fulladdad!");
            }
        }
        else
        {
            Console.WriteLine("Detta fordon har inget batteri.");
        }
    }
    // Enkel metod som ser till att Användaren skriver ett giltigt heltal
    int SafeInteger()
    {
        int number = 0;
        bool isGood = false;

        while (!isGood)
        {
            try { 
                number = int.Parse(Console.ReadLine());
                isGood = true;
            } 
            catch { Console.WriteLine("Felaktig inmatning. Ange ett giltigt nummer:"); }
        }
        return number;
    }
}
