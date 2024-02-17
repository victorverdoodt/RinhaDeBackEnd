using RinhaDeBackEnd.Attributes;
using System.ComponentModel.DataAnnotations;

namespace RinhaDeBackEnd.Domain.Dtos
{
    public class TransactionDto
    {
        [Required]
        [Positive(ErrorMessage = "O valor deve ser positivo.")]
        public int Valor { get; set; }

        [Required(AllowEmptyStrings = false)]
        [RegularExpression("^[cd]$", ErrorMessage = "O Tipo deve ser 'c' ou 'd'.")]
        public char Tipo { get; set; }

        [Required(AllowEmptyStrings = false)]
        [StringLength(10, MinimumLength = 1, ErrorMessage = "A descrição precisa ser entre 1 e 10 caracteres.")]
        public string Descricao { get; set; }
    }
}
