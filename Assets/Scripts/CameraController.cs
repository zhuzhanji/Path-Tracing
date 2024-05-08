using UnityEngine;
using System.Collections;
 
public class CameraController : MonoBehaviour {
 
    /*
    Writen by Windexglow 11-13-10.  Use it, edit it, steal it I don't care.  
    Converted to C# 27-02-13 - no credit wanted.
    Simple flycam I made, since I couldn't find any others made public.  
    Made simple to use (drag and drop, done) for regular keyboard layout  
    wasd : basic movement
    shift : Makes camera accelerate
    space : Moves camera on X and Z axis only.  So camera doesn't gain any height*/
    
    // public Camera myCamera;
    public float fov;

    public float minFov = 1f, maxFov = 90f, sensitivity = 5f;
    public float mainSpeed = 50.0f; //regular speed
    public float camSens = 0.15f; //How sensitive it with mouse
    public float shiftAdd = 100.0f; //multiplied by how long shift is held.  Basically running
    public float maxShift = 1000.0f; //Maximum speed when holdin gshift
    private Vector3 lastMouse = new Vector3(255, 255, 255); //kind of in the middle of the screen, rather than at the top (play)
    private float totalRun= 1.0f;

    public float rotateSpeed = 1f;
    public float vertSpeed = .5f;
    private bool locked = false;
    private RayTracingController rtc;

    void Awake() {
        rtc = gameObject.GetComponent<RayTracingController>();
        Camera.main.fieldOfView = fov;
    }
     
    void Update () {
        if (Input.GetKeyDown(KeyCode.Space)) {
            locked = !locked ;
            if (!locked)
                rtc.MaxReflections = rtc.maxReflectionsUnlocked;
            else
                rtc.MaxReflections = rtc.maxReflectionsLocked;
        }
        
        if (locked) {
            lastMouse =  Input.mousePosition;
            return;
        }

        // update fov
        float fov = Camera.main.fieldOfView;
        fov += Input.GetAxis("Mouse ScrollWheel") * 5f;
        fov = Mathf.Clamp(fov, minFov, maxFov);
        if (Mathf.Abs(fov - Camera.main.fieldOfView) > float.Epsilon) {
            rtc.transform.hasChanged = true;
            Camera.main.fieldOfView = fov;
        }


        lastMouse = Input.mousePosition - lastMouse ;
        lastMouse = new Vector3(-lastMouse.y * camSens, lastMouse.x * camSens, 0 );
        lastMouse = new Vector3(transform.eulerAngles.x + lastMouse.x , transform.eulerAngles.y + lastMouse.y, 0);
        transform.eulerAngles = lastMouse;
        lastMouse =  Input.mousePosition;
        //Mouse  camera angle done.  
       
        //Keyboard commands
        // float f = 0.0f;
        Vector3 p = GetBaseInput();
        if (p.sqrMagnitude > 0){ // only move while a direction key is pressed
          if (Input.GetKey (KeyCode.LeftShift)){
              totalRun += Time.deltaTime;
              p  = p * totalRun * shiftAdd;
              p.x = Mathf.Clamp(p.x, -maxShift, maxShift);
              p.y = Mathf.Clamp(p.y, -maxShift, maxShift);
              p.z = Mathf.Clamp(p.z, -maxShift, maxShift);
          } else {
              totalRun = Mathf.Clamp(totalRun * 0.5f, 1f, 1000f);
              p = p * mainSpeed;
          }
         
          p = p * Time.deltaTime;
          Vector3 newPosition = transform.position;
          if (Input.GetKey(KeyCode.Space)){ //If player wants to move on X and Z axis only
              transform.Translate(p);
              newPosition.x = transform.position.x;
              newPosition.z = transform.position.z;
              transform.position = newPosition;
          } else {
              transform.Translate(p);
          }
        }

        if (Input.GetKey(KeyCode.LeftArrow))
            transform.Rotate(-Vector3.up * rotateSpeed * Time.deltaTime);
            
        if (Input.GetKey(KeyCode.RightArrow))
            transform.Rotate(Vector3.up * rotateSpeed * Time.deltaTime);

        if (Input.GetKey(KeyCode.UpArrow))
            transform.Rotate(-Vector3.right * rotateSpeed * .75f * Time.deltaTime);
            
        if (Input.GetKey(KeyCode.DownArrow))
            transform.Rotate(Vector3.right * rotateSpeed * .75f * Time.deltaTime);

        if (Input.GetKey (KeyCode.Q))
           transform.Translate(Vector3.up * vertSpeed);

        if (Input.GetKey (KeyCode.E))
           transform.Translate(Vector3.down * vertSpeed);
    }
     
    private Vector3 GetBaseInput() { //returns the basic values, if it's 0 than it's not active.
        Vector3 p_Velocity = new Vector3();
        if (Input.GetKey (KeyCode.W)){
            p_Velocity += new Vector3(0, 0 , 1);
        }
        if (Input.GetKey (KeyCode.S)){
            p_Velocity += new Vector3(0, 0, -1);
        }
        if (Input.GetKey (KeyCode.A)){
            p_Velocity += new Vector3(-1, 0, 0);
        }
        if (Input.GetKey (KeyCode.D)){
            p_Velocity += new Vector3(1, 0, 0);
        }
        return p_Velocity;
    }
}