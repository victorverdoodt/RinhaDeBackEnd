using System.ComponentModel.DataAnnotations;

namespace RinhaDeBackEnd.Attributes
{
    public class PositiveAttribute : ValidationAttribute
    {
        public override bool IsValid(object value)
        {
            if (value == null) return false;

            // Check if the type is numeric and if it is positive
            return value switch
            {
                int i => i > 0,
                long l => l > 0,
                float f => f > 0,
                double d => d > 0,
                decimal dec => dec > 0,
                _ => false
            };
        }

        public override string FormatErrorMessage(string name)
        {
            return $"The field {name} must be a positive number.";
        }
    }

}
