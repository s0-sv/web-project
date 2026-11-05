using System.ComponentModel.DataAnnotations;

namespace TutorHub.Contracts;

public class PageQuery
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    [Range(0, int.MaxValue)]
    public int Skip { get; set; }

    [Range(1, MaxLimit)]
    public int Limit { get; set; } = DefaultLimit;
}
