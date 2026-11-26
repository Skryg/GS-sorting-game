using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A category tile in the GameSetupMenu list (template: Setup Panel/CategoryList/Viewport/Content/CategoryButtonTemplate).
public class CategoryButtonView : MonoBehaviour
{
    public Button button;
    public Image icon;
    public TextMeshProUGUI nameLabel;
    [Tooltip("Dot in the color of the bin the category is assigned to")]
    public Image colorBadge;
    [Tooltip("Frame shown when the category belongs to the selected color")]
    public GameObject selection;

    [System.NonSerialized] public ObjectCategory category;
}
