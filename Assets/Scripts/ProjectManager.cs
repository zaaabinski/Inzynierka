using UnityEngine;

public class ProjectManager : MonoBehaviour
{
    public static ProjectManager _instance;

    private void Awake()
    {

        if(_instance==null)
            _instance = this;

        else 
            Destroy(this);
    }
}
