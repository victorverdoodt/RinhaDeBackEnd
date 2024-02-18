using System.ComponentModel.DataAnnotations;

namespace RinhaDeBackEnd.Attributes
{
    public class DescriptionAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value)
        {
            if (value == null) return false;

            if(string.IsNullOrEmpty(value.ToString())) return false;

            if(value.ToString().Length > 10) return false;

            return true;
        }
    }
}
