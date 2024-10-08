namespace Cards{
public enum CardUnit
{
    MODE_SINGLE = 1,  // "single"
    MODE_PAIR = 2,  // "pair"
    MODE_THREE = 3,  // "three"
    MODE_THREE_ONE = 4,  // "three1"  8883
    MODE_THREE_TWE = 5,  // "three2"   88833
    MODE_AIRPLANE_NONE = 6,  // MODE_THREE + "s"  # 2*3  555666
    MODE_AIRPLANE_ONE = 7,  // MODE_THREE_ONE + "s"  # 2*4  55576668
    MODE_AIRPLANE_TWE = 8,  // MODE_THREE_TWE + "s"  # 2*5   5557766688
    MODE_SINGLE_LONG = 9,  // "single_long"  # 6   345678
    MODE_PAIR_LONG = 10,  // "pair_long"  # 6    334455
    MODE_BOMB = 11,  // "bombs"  # 4   3333
    MODE_TWO_RED2 = 12,  // "king2"  # 2   22
    MODE_INVALID = -1   // "invalid"
}

public class CardMode
{
    public static CardUnit Value(List<int> cards)
    {
        cards.Sort((a, b) => b.CompareTo(a)); // Sort in descending order

        HashSet<int> valueSet = new HashSet<int>();
        foreach (var card in cards)
        {
            valueSet.Add(Card.AdjustValue(card)); // Assuming Card.AdjustValue is defined elsewhere
        }

        int cnt = cards.Count;
        int valueSetLen = valueSet.Count;

        string outputResult = String.Join(" ", cards);
        Console.WriteLine($"Sorted order is {outputResult}");
        Console.WriteLine($"Cards {cnt}, catagories {valueSetLen}");
        if (cnt == 1)
        {
            return CardUnit.MODE_SINGLE;
        }
        else if (cnt == 2 && valueSetLen == 1)
        {
            if (cards.SequenceEqual(new List<int> { 48, 48 }))
            {
                return CardUnit.MODE_TWO_RED2;
            }
            else
            {
                return CardUnit.MODE_PAIR;
            }
        }
        else if (cnt == 3 && valueSetLen == 1)
        {
            return CardUnit.MODE_THREE;
        }
        else if (cnt == 4)
        {
            if (valueSetLen == 2)
            {
                if (cards[0] / 4 == cards[1] / 4 && cards[1] / 4 == cards[2] / 4 || 
                    cards[3] / 4 == cards[1] / 4 && cards[1] / 4 == cards[2] / 4)
                {
                    return CardUnit.MODE_THREE_ONE;
                }
                else
                {
                    return CardUnit.MODE_INVALID;
                }
            }
            else if (valueSetLen == 1)
            {
                return CardUnit.MODE_BOMB;
            }
            else
            {
                return CardUnit.MODE_INVALID;
            }
        }
        else if (cnt == 5)
        {
            if (valueSetLen == 2)
            {
                if ((cards[0] / 4 == cards[1] / 4 && cards[1] / 4 == cards[2] / 4 && cards[4] / 4 == cards[3] / 4) || 
                    (cards[0] / 4 == cards[1] / 4 && cards[2] / 4 == cards[3] / 4 && cards[3] / 4 == cards[4] / 4))
                {
                    return CardUnit.MODE_THREE_TWE;
                }
                else
                {
                    return CardUnit.MODE_INVALID;
                }
            }
            else if (valueSetLen == 1)
            {
                return CardUnit.MODE_BOMB;
            }
            else if (valueSetLen == 5)
            {
                return CardUnit.MODE_SINGLE_LONG;
            }
            else
            {
                return CardUnit.MODE_INVALID;
            }
        }
        else if (cnt >= 6)
        {
            if (valueSetLen == cnt)
            {
                return CardUnit.MODE_SINGLE_LONG;
            }
            if (valueSetLen == cnt / 2)
            {
                // TODO more detail
                return CardUnit.MODE_PAIR_LONG;
            }
            if (valueSetLen == cnt / 3)
            {
                return CardUnit.MODE_AIRPLANE_NONE;
            }
            if (valueSetLen == 1)
            {
                return CardUnit.MODE_BOMB;
            }
            if (cnt % 5 == 0 && cnt / 5 <= valueSetLen && valueSetLen <= cnt / 5 * 2)
            {
                return CardUnit.MODE_AIRPLANE_TWE;
            }
            if (cnt % 4 == 0 && cnt / 4 <= valueSetLen && valueSetLen <= cnt / 4 * 2)
            {
                return CardUnit.MODE_AIRPLANE_ONE;
            }

            return CardUnit.MODE_INVALID;
        }
        else
        {
            return CardUnit.MODE_INVALID;
        }
    }

    public void Main(String[] args) {
        Console.WriteLine("One");

    }
}

}
