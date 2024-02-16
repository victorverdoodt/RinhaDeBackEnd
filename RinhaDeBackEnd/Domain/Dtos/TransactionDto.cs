using System.ComponentModel.DataAnnotations;

namespace RinhaDeBackEnd.Domain.Dtos
{
    public class TransactionDto
    {
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Valor precisa ser positivo.")]
        public int Valor { get; set; }

        [Required(AllowEmptyStrings = false)]
        [RegularExpression("^[cd]$", ErrorMessage = "O Tipo deve ser 'c' ou 'd'.")]
        public string Tipo { get; set; }

        [Required(AllowEmptyStrings = false)]
        [StringLength(10, MinimumLength = 1, ErrorMessage = "A descrição precisa ser entre 1 e 10 caracteres.")]
        public string Descricao { get; set; }
    }
}
