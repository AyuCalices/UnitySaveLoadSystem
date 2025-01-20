using Sample.Scripts;
using UnityEngine;

[CreateAssetMenu]
public class ItemGenerator : ScriptableObject
{
    public SpriteLookup spriteLookup;
    public Sprite sprite;
    public string itemName;
    
    public Item GenerateItem()
    {
        return new Item(spriteLookup, sprite, itemName);
    }
}
