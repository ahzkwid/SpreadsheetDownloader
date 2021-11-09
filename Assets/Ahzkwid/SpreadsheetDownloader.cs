using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;






#if UNITY_EDITOR

using UnityEditor;
using UnityEditorInternal;
[CustomEditor(typeof(SpreadsheetDownloader))]
public class SpreadsheetDownloaderEditor : Editor
{

    Hashtable reorderableListTable = new Hashtable();
    public static System.Type GetType(SerializedProperty property)
    {
        var parentType = property.serializedObject.targetObject.GetType();
        var fieldInfo = parentType.GetField(property.propertyPath);
        return fieldInfo.FieldType;
    }
    public void DrawReorderableList(string propertyPath)
    {

        var reorderableListProperty = serializedObject.FindProperty(propertyPath);

        if (reorderableListTable[propertyPath] == null)
        {
            reorderableListTable[propertyPath] = new ReorderableList(serializedObject, reorderableListProperty);
        }
        var reorderableList = (ReorderableList)reorderableListTable[propertyPath];

        serializedObject.Update();
        {

            //헤더명
            reorderableList.drawHeaderCallback = (rect) => EditorGUI.LabelField(rect, $"{propertyPath} ({reorderableListProperty.arraySize})");


            //요소
            var targetType = GetType(reorderableListProperty).GetElementType();

            reorderableList.elementHeight = EditorGUIUtility.singleLineHeight * Mathf.Max(1, targetType.GetFields().Length);

            reorderableList.drawElementCallback =
            (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var elementProperty = reorderableListProperty.GetArrayElementAtIndex(index);

                var fieldRect = rect;
                fieldRect.height = EditorGUIUtility.singleLineHeight;


                fieldRect.y -= EditorGUIUtility.singleLineHeight;
                foreach (var fields in targetType.GetFields())
                {
                    fieldRect.y += EditorGUIUtility.singleLineHeight;
                    EditorGUI.PropertyField(fieldRect, elementProperty.FindPropertyRelative(fields.Name));
                }

            };
            reorderableList.DoLayoutList();



        }
        serializedObject.ApplyModifiedProperties();
    }





    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        GUILayout.Space(20);
        GUILayout.Label("스프레드 시트 리스트");
        DrawReorderableList(nameof(SpreadsheetDownloader.spreadsheets));

        GUILayout.Space(20);
        if (GUILayout.Button("폴더열기"))
        {
            var folderPath = (target as SpreadsheetDownloader).GetExtractFolderPath();
            System.Diagnostics.Process.Start(folderPath);
        }

    }
}
#endif






/// <summary>
/// 2021-11-10 ver (creator : ahzkwid)
/// </summary>
public class SpreadsheetDownloader : MonoBehaviour
{
    [System.Serializable]
    public class IntEvent : UnityEvent<int[]> { }
    [Header("다운로드 실패를 알림, 실패한 이유들을 Error코드로 반환")]
    public IntEvent OnDownloadFailEvent;

    [System.Serializable]
    public class StringsEvent : UnityEvent<string[]> { }
    [Header("모든 다운로드가 끝났음을 알림,filePath들을 반환")]
    public StringsEvent OnDownloadSuccessEvent;


    [HideInInspector]
    public Spreadsheet[] spreadsheets;
    public string folderPath = "/Spreadsheets";

    [System.Serializable]
    public class Spreadsheet
    {

        public string sheetName = "";
        public string key = "";
        public string Link
        {
            get
            {
                return "https://" + $"docs.google.com/spreadsheets/d/{key}/gviz/tq?tqx=out:csv&sheet={sheetName}";
            }
        }
    }

    public enum ErrorCode
    {
        NoPermission, WriteFileFail
    }



    // Start is called before the first frame update
    void Start()
    {
    }

    public string GetExtractFolderPath()
    {
        return $"{Application.persistentDataPath + folderPath}";
    }
    IEnumerator[] requests = null;
    public void DownLoadStart()
    {
        if (requests != null)
        {
            return;
        }
        requests = DownLoadStart(spreadsheets);
    }
    public IEnumerator[] DownLoadStart(Spreadsheet[] spreadsheets)
    {
        var requests = System.Array.ConvertAll(spreadsheets, spreadsheet =>
        {
            var filePath = $"{GetExtractFolderPath()}/{spreadsheet.sheetName}.csv";
            var downloadLink = spreadsheet.Link;
            return DownloadFile(downloadLink, filePath);
        });
        foreach (var request in requests)
        {
            StartCoroutine(request);
        }
        StartCoroutine(WaitDownloadSuccess(requests));
        return requests;
    }
    string[] GetFilePaths(Spreadsheet[] spreadsheets)
    {
        return System.Array.ConvertAll(spreadsheets, x => $"{GetExtractFolderPath()}/{x.sheetName}.csv");
    }
    IEnumerator WaitDownloadSuccess(IEnumerator[] requests)
    {
        yield return new WaitUntil(() => CheckDownloadSuccess(requests));
        yield return new WaitForEndOfFrame();

        var successRequests = System.Array.FindAll(requests, request => request.Current is string);
        var failRequests = System.Array.FindAll(requests, request => request.Current is ErrorCode);

        var successFilePaths = System.Array.ConvertAll(successRequests, request => (string)request.Current);
        var failFilePaths = System.Array.ConvertAll(failRequests, request => (int)request.Current);


        if (failFilePaths.Length > 0)
        {
            OnDownloadFailEvent.Invoke(failFilePaths);
        }
        if (successFilePaths.Length > 0)
        {
            OnDownloadSuccessEvent.Invoke(successFilePaths);
        }
    }
    bool CheckDownloadSuccess(IEnumerator[] requests)
    {
        return System.Array.TrueForAll(requests, request => (request.Current is string) || (request.Current is ErrorCode));
    }


    /// <summary>
    /// 다운 다되면 파일경로를 반환
    /// </summary>
    /// <param name="downloadlink"></param>
    /// <param name="filePath"></param>
    /// <returns></returns>
    IEnumerator DownloadFile(string downloadlink, string filePath)
    {
        Debug.Log($"Download:{downloadlink}");
        {
            var unityWebRequest = UnityWebRequest.Get(downloadlink);

            DOWNLOAD_RETRY:;
            var operation = unityWebRequest.SendWebRequest();
            yield return new WaitUntil(() => operation.isDone);

            if (unityWebRequest.isNetworkError)
            {
                Debug.LogError(unityWebRequest.error);
                Debug.LogError("다운로드 실패. 재시도중");
                yield return new WaitForSeconds(1f);
                goto DOWNLOAD_RETRY;
            }
            else
            {
                Debug.Log(filePath);

                var folderPath = System.IO.Path.GetDirectoryName(filePath);
                Debug.Log(folderPath);
                if (System.IO.Directory.Exists(folderPath) == false)//폴더가 없으면 생성
                {
                    System.IO.Directory.CreateDirectory(folderPath);
                }
                var text = unityWebRequest.downloadHandler.text;
                if (text.Contains("<!DOCTYPE html>") || text.Contains("<head>") || text.Contains("<style>"))
                {
                    Debug.LogError($"text:{text}");
                    Debug.LogError("다운로드 권한 없음");
                    yield return ErrorCode.NoPermission;
                    yield break;
                    //yield return new WaitForSeconds(3f);
                    //goto DOWNLOAD_RETRY;
                }


                bool writeSuccess = false;
                try
                {
                    System.IO.File.WriteAllBytes(filePath, unityWebRequest.downloadHandler.data);
                    writeSuccess = true;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError(ex);
                    Debug.LogError("파일 접근 불가");
                }
                if (writeSuccess == false)
                {
                    yield return ErrorCode.WriteFileFail;
                    yield break;
                }
            }
        }
        yield return filePath;
    }
    // Update is called once per frame
    void Update()
    {

    }
}
