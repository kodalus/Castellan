using Castellan.Application.Services;
using Castellan.Domain.ValueObjects;
using FluentAssertions;

namespace Castellan.Application.Tests;

/// <summary>
/// Zakładki Shella powstają raz i żyją tak długo jak aplikacja, a telefon trzyma ją
/// w pamięci tygodniami. Miesiąc ustawiony raz w konstruktorze zostawał więc na
/// wrześniu po 1 października.
/// </summary>
public class MonthCursorTests
{
    private static readonly YearMonth September = new(2026, 9);
    private static readonly YearMonth October = new(2026, 10);
    private static readonly YearMonth November = new(2026, 11);

    [Fact]
    public void A_screen_opened_last_month_shows_the_new_month_on_return()
    {
        var today = September;
        var cursor = new MonthCursor(() => today);

        cursor.Month.Should().Be(September);

        today = October;

        cursor.Refresh().Should().Be(October);
    }

    [Fact]
    public void A_month_the_user_chose_is_not_taken_away_from_them()
    {
        var today = October;
        var cursor = new MonthCursor(() => today);

        // Cofnięcie się po historię to decyzja użytkownika. Przeskok na bieżący miesiąc
        // przy każdym wejściu na ekran zabierałby mu to, co właśnie chciał obejrzeć.
        cursor.Previous().Should().Be(September);

        cursor.Refresh().Should().Be(September);
    }

    [Fact]
    public void Coming_back_to_this_month_resumes_following_the_calendar()
    {
        var today = October;
        var cursor = new MonthCursor(() => today);

        cursor.Previous().Should().Be(September);
        cursor.Next().Should().Be(October);

        // Bez tego jedno zerknięcie we wrzesień przypinałoby ekran do października
        // na zawsze — i w listopadzie znów trzeba by przełączać ręcznie.
        today = November;

        cursor.Refresh().Should().Be(November);
    }

    [Fact]
    public void Browsing_the_future_also_stops_the_following()
    {
        var today = October;
        var cursor = new MonthCursor(() => today);

        cursor.Next().Should().Be(November);

        cursor.Refresh().Should().Be(November);
    }
}
