namespace Kajens_Cykeluthyrning
{
    public class Customer
    {
        public string Name;
        public int Age;
        public bool IsMember;

        public Customer(string name, int age, bool isMember)
        {
            Name = name;
            Age = age;
            IsMember = isMember;
        }
    }
}