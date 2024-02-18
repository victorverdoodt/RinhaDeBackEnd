using RinhaDeBackEnd.Attributes;
using System.ComponentModel.DataAnnotations;

namespace RinhaDeBackEnd.Domain.Dtos
{
    public class TransactionDto
    {
        [Required]
        [Positive]
        public int Valor { get; set; }

        [Required(AllowEmptyStrings = false)]
        [RegularExpression("^[cd]$")]
        public char Tipo { get; set; }

        [Required(AllowEmptyStrings = false)]
        [Description]
        public string Descricao { get; set; }
    }
}
