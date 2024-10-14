namespace Cards {

public class Card
{
    private static List<int> all_cards = Enumerable.Range(0, 52).Concat(Enumerable.Range(0, 52)).ToList();
    
    public static int AdjustValue(int card)
    {
        int value = card / 4;
        return value;
    }

    public static List<int> Shuffle() {
        Random rng = new Random();
        return all_cards.OrderBy(card => rng.Next()).ToList();
    }

    public static int SingleCardCompare(int card1, int card2)
    {
        int value1 = AdjustValue(card1);
        int value2 = AdjustValue(card2);
        if (value1 < value2)
        {
            return -1;
        }
        else if (value1 > value2)
        {
            return 1;
        }
        else
        {
            return 0;
        }
    }
}

}