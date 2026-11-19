using Castellan.Application.Services;
using FluentAssertions;

namespace Castellan.Application.Tests;

/// <summary>
/// Lupka na liście transakcji. Szukanie ma działać tak, jak się pisze na telefonie:
/// bez ogonków, bez pilnowania kolejności słów i bez zgadywania, czy kwota ma kropkę
/// czy przecinek. Wiersz, który wraca z wyszukiwania, musi WIDOCZNIE zawierać to,
/// czego się szukało — dlatego przeszukujemy dokładnie te pola, które widać.
/// </summary>
public class TransactionSearchTests
{
    private const string Kategoria = "Zdrowie i apteka";
    private const string Notatka   = "zwrot za ortopedę";
    private const string Kwota     = "200,00 zł";
    private const string Data      = "18.11.\n2026";

    private static bool Szukaj(string query) =>
        TransactionSearch.Matches(query, Data, Kategoria, Notatka, Kwota);

    [Fact]
    public void An_empty_query_matches_everything()
    {
        Szukaj("").Should().BeTrue();
        Szukaj("   ").Should().BeTrue();
    }

    [Theory]
    [InlineData("ortoped")]
    [InlineData("apteka")]
    [InlineData("ZDROWIE")]
    public void A_fragment_of_any_visible_field_matches(string query)
        => Szukaj(query).Should().BeTrue();

    [Fact]
    public void Polish_letters_match_without_their_marks_and_the_other_way_round()
    {
        Szukaj("ortopede").Should().BeTrue("nikt nie przełącza klawiatury w środku szukania");
        Szukaj("ortopedę").Should().BeTrue();
        TransactionSearch.Matches("wplata", "Wpłata partnera").Should().BeTrue("„ł” nie rozkłada się jak reszta ogonków");
        TransactionSearch.Matches("wpłata", "Wplata partnera").Should().BeTrue();
    }

    [Fact]
    public void A_dot_and_a_comma_are_the_same_character()
    {
        Szukaj("200.00").Should().BeTrue();
        Szukaj("200,00").Should().BeTrue();
        Szukaj("18.11").Should().BeTrue();
    }

    [Fact]
    public void Every_word_has_to_match_but_the_order_does_not()
    {
        Szukaj("zwrot apteka").Should().BeTrue();
        Szukaj("apteka zwrot").Should().BeTrue();
        Szukaj("apteka paliwo").Should().BeFalse("drugie słowo nie pasuje do niczego w wierszu");
    }

    [Fact]
    public void Empty_fields_do_not_break_the_match()
        => TransactionSearch.Matches("lidl", null, "Lidl", "", null).Should().BeTrue();
}
