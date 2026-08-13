namespace FlowerWms.Tsd.Models;

// Статусы коробки
public enum BoxStatus
{
    Draft = 0,      // Черновик
    Active = 1,     // Активная
    Empty = 2,      // Пустая
    Shipped = 3,    // Отгружена
    Discarded = 4,  // Списана
    Reserved = 5    // Зарезервирована
}