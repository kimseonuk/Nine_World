using UnityEngine;
using UnityEngine.SceneManagement;

// Player hit points. Monsters call TakeHit; at 0 HP the player goes down, the screen fades out and the stage restarts
// so monsters are reset too. Draws the health bar and a red flash when hit.
public class PlayerHealth : MonoBehaviour
{
    public int maxHealth = 100;
    public float invulnerableTime = 0.8f;
    [Tooltip("Seconds from death to the restart; long enough for the death clip to play out.")]
    public float deathDelay = 6f;
    [Tooltip("The screen fades to black over the last seconds of the death delay.")]
    public float deathFade = 1.5f;

    public int Health { get; private set; }
    public bool IsDead => Health <= 0;

    float invulnerableTimer;
    float hitFlash;
    float deathTimer = -1f;
    float shownHealth;          // the bar drains smoothly toward the real value
    PlayerController controller;
    GUIStyle labelStyle, bigStyle;

    void Start()
    {
        Health = maxHealth;
        shownHealth = maxHealth;
        controller = GetComponent<PlayerController>();
    }

    void Update()
    {
        if (invulnerableTimer > 0f) invulnerableTimer -= Time.deltaTime;
        if (hitFlash > 0f) hitFlash -= Time.deltaTime;
        shownHealth = Mathf.MoveTowards(shownHealth, Mathf.Max(Health, 0), maxHealth * 0.8f * Time.deltaTime);
        if (deathTimer >= 0f)
        {
            deathTimer += Time.deltaTime;
            if (deathTimer >= deathDelay) SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }

    public void TakeHit(int damage)
    {
        // no hits while the controls are locked for the boss intro shot, or during a dodge's invincibility frames
        if (invulnerableTimer > 0f || IsDead) return;
        if (controller != null && (controller.movementLocked || controller.IsInvulnerable)) return;
        Health -= damage;
        invulnerableTimer = invulnerableTime;
        hitFlash = 0.25f;
        Debug.Log($"Player hit: -{damage} HP ({Health}/{maxHealth})");
        if (Health <= 0)
        {
            Debug.Log("Player defeated - restarting stage");
            deathTimer = 0f;
            if (controller != null) controller.movementLocked = true;
        }
    }

    void OnGUI()
    {
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            labelStyle.normal.textColor = Color.white;
            bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 48, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        // red edge flash when hit
        if (hitFlash > 0f)
        {
            GUI.color = new Color(0.9f, 0.05f, 0f, 0.35f * hitFlash / 0.25f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        }

        // health bar, bottom left
        Rect bar = new Rect(30f, Screen.height - 50f, 300f, 20f);
        GUI.color = new Color(0f, 0f, 0f, 0.7f); GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = new Color(0.85f, 0.2f, 0.1f);   // the part just lost, draining away
        GUI.DrawTexture(new Rect(bar.x + 2, bar.y + 2, (bar.width - 4) * Mathf.Clamp01(shownHealth / maxHealth), bar.height - 4), Texture2D.whiteTexture);
        GUI.color = new Color(0.3f, 0.85f, 0.35f);
        GUI.DrawTexture(new Rect(bar.x + 2, bar.y + 2, (bar.width - 4) * Mathf.Clamp01((float)Health / maxHealth), bar.height - 4), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(bar.x, bar.y - 22f, 200f, 22f), $"HP {Mathf.Max(Health, 0)} / {maxHealth}", labelStyle);

        // fade out once the death clip has played out
        if (deathTimer >= 0f)
        {
            float k = Mathf.Clamp01((deathTimer - (deathDelay - deathFade)) / deathFade);
            GUI.color = new Color(0f, 0f, 0f, k * 0.9f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            bigStyle.normal.textColor = new Color(0.95f, 0.3f, 0.2f, Mathf.Clamp01(k * 2f));
            GUI.color = Color.white;
            GUI.Label(new Rect(0f, Screen.height * 0.4f, Screen.width, 60f), "쓰러졌다...", bigStyle);
        }
        GUI.color = Color.white;
    }
}
