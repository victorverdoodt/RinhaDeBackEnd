using RinhaDeBackEnd_AOT.Attributes;
using System.ComponentModel.DataAnnotations;

namespace RinhaDeBackEnd_AOT.Dto
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

        public DateTime? Realizada_em { get; set; }
    }
}
