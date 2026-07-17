using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using YARG.Menu.ProfileList;

[InitializeOnLoad]
internal static class CreateSongVotingProfileSetting
{
    private const string PrefabPath = "Assets/Prefabs/Menu/ProfileList/ProfileListMenu.prefab";

    static CreateSongVotingProfileSetting()
    {
        EditorApplication.delayCall += Create;
    }

    public static void Create()
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var sidebar = prefabRoot.GetComponentInChildren<ProfileSidebar>(true);
            var sidebarProperties = new SerializedObject(sidebar);
            var songVotingProperty = sidebarProperties.FindProperty("_songVotingToggle");
            if (songVotingProperty.objectReferenceValue != null)
            {
                return;
            }

            var rangeToggle = sidebarProperties.FindProperty("_rangeDisabledToggle").objectReferenceValue as Toggle;
            var rangeOption = rangeToggle.transform.parent.parent;
            var option = Object.Instantiate(rangeOption.gameObject, rangeOption.parent);
            option.name = "Song Voting";
            option.transform.SetSiblingIndex(rangeOption.GetSiblingIndex() + 1);
            option.transform.Find("Option Name").GetComponent<TextMeshProUGUI>().text =
                "PARTICIPATE IN SONG VOTING";

            var songVotingToggle = option.GetComponentInChildren<Toggle>(true);
            var toggleProperties = new SerializedObject(songVotingToggle);
            toggleProperties.FindProperty("onValueChanged.m_PersistentCalls.m_Calls").ClearArray();
            toggleProperties.ApplyModifiedPropertiesWithoutUndo();

            songVotingProperty.objectReferenceValue = songVotingToggle;
            sidebarProperties.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }
}
