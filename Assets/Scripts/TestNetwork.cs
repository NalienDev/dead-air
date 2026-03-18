using PurrNet;
using UnityEngine;

public class TestNetwork : NetworkIdentity
{
    [SerializeField] private Color _color;
    [SerializeField] private Renderer _renderer;
    [SerializeField] private TextMesh _healthText;
    [SerializeField] private SyncVar<int> _health = new(100); //Necess�rio fazer new

    [SerializeField] private int _localHealth = 100;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.A))
        {
            SetHealth(_localHealth - 10);
        }
        if (Input.GetKeyDown(KeyCode.S))
        {
            TakeDamage(10);
        }
    }

    private void Awake()
    {
        _health.onChanged += OnHealthChanged;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        _health.onChanged -= OnHealthChanged;
    }

    private void OnHealthChanged(int newValue)
    {
        //Debug.Log(newValue);
        _healthText.text = newValue.ToString();
    }

    [ServerRpc]
    private void TakeDamage(int damage) { 
        _health.value -= damage; //necess�rio fazer .value em SyncVars
    }

    [ObserversRpc(bufferLast:true)]
    private void SetHealth(int health)
    {
        _localHealth = health;
    }

    protected override void OnObserverAdded(PlayerID player) // Isto � chamado quando um novo cliente entra no servidor
    {
        base.OnObserverAdded(player);
    }

    /*
     * RPC instrui quem vai correr o codigo, neste caso estamos a dizer a todos os clientes para correrem este codigo
     * ServerRpc: Client (ou Server) -> Server
     * ObserversRpc: Server (ou Client) -> All Clients
     * TargetRpc: Server (Ou Client) -> Single client
     */

    [ObserversRpc(bufferLast:true)] // bufferlast faz com que quando um novo cliente entra, corre o ultimo codigo a ser corrido na network
    private void SetColor(Color color) // Pode ser enviado tudo pela network, gameObjects precisam de ter uma network identity, strucks tambem podem ser enviados (tipo objetos de uma classe)
    {
        _renderer.material.color = color;
    }

    /*
    [SerializeField] private NetworkIdentity _networkIdentity;
    
    protected override void OnSpawned() // � necess�rio instanciar objetos aqui para apareceram para o cliente e o servidor,
                                        // se forem instanciados no awake, o cliente e o servidor ter�o objetos diferentes apesar de terem o mesmo id,
                                        // e n�o aparecer�o para o cliente nem para o servidor
    {
        
        base.OnSpawned();

        if (!isServer)
            return;

        Instantiate(_networkIdentity, Vector3.zero, Quaternion.identity);
        
    }
    */
}
