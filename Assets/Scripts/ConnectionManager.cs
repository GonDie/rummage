
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SocketIO;

public class ConnectionManager : Singleton<ConnectionManager> {

    const string UPDATE_PLAYERS = "updatePlayers";
    const string UPDATE_GAMEDATA = "updateGameData";
    const string JOIN_SUCCESS = "onJoinSucess";

    private SocketIOComponent socket;
    string sessionId;
    int playerAvatar = 0;
    string playerName = "";
    bool _isHost = false;
    string _userId;
    public bool IsHost { get => _isHost; }

    void Start () {
        socket = GetComponent<SocketIOComponent>();
        socket.On("newSession", NewSession);
        socket.On("error", OnError);
		socket.On("close", OnClose);

        socket.On(UPDATE_PLAYERS, OnUpdatedPlayers);
        socket.On(UPDATE_GAMEDATA, OnUpdateGameData);
        socket.On(JOIN_SUCCESS, OnJoinSucess);
        
        Events.OnCreateSession += OnCreateSession;
        Events.OnAvatarSelect += OnAvatarSelect;
        Events.OnPlayerNameChange += OnPlayerNameChange;
        Events.OnWaitingMenu += OnWaitingMenu;
        Events.OnSessionChange += OnSessionChange;
        Events.OnJoinSession += OnJoinSession;
    }

    public void OnError(SocketIOEvent e) {
		Debug.Log(e);
		Debug.Log("[SocketIO] Error received: " + e.name + " " + e.data);
	}
	
	public void OnClose(SocketIOEvent e) {	
		Debug.Log("[SocketIO] Close received: " + e.name + " " + e.data);
	}

    void OnDestroy () {
        Events.OnCreateSession -= OnCreateSession;
        Events.OnAvatarSelect -= OnAvatarSelect;
        Events.OnPlayerNameChange -= OnPlayerNameChange;
        Events.OnSessionChange -= OnSessionChange;
        Events.OnWaitingMenu -= OnWaitingMenu;
        Events.OnJoinSession -= OnJoinSession;
    }

    void OnCreateSession () {
        socket.Emit("createSession");
        socket.On("joinupdate", Joinupdate);
        socket.On("updatedAvatar", OnUpdateAvatar);
        GameManager.Instance.Players.Clear();
        _isHost = true;
    }

    void OnAvatarSelect (int avatar) {
        playerAvatar = avatar;
    }

    void OnPlayerNameChange (string name) {
        playerName = name;
    }

    void OnSessionChange (string name) {
        sessionId = name;
    }

    void OnJoinSession () {
        _isHost = false;
        GameManager.Instance.Players.Clear();

        Debug.Log("join session: " + sessionId);
        JSONObject data = new JSONObject(JSONObject.Type.OBJECT);
        data.AddField("sessionId", sessionId);
        socket.Emit("join", data);
    }

    void OnJoinSucess (SocketIOEvent e) {
        e.data.GetField("userId", delegate(JSONObject data) {
            Events.OnAvatarsMenu?.Invoke();
            _userId = data.str;
            Debug.Log("sucess join: " + _userId);
        }, delegate(string name) {
            Debug.LogWarning("no game sessions");
        });
    }

    void OnWaitingMenu () {
        if (_isHost) {
            GameManager.Instance.Players[0].name = playerName;
            GameManager.Instance.Players[0].avatar = playerAvatar;
            StartCoroutine(UpdateWaitingMenu());
        } else {
            JSONObject data = new JSONObject(JSONObject.Type.OBJECT);
            data.AddField("name", playerName);
            data.AddField("avatar", playerAvatar);
            data.AddField("sessionId", sessionId);

            socket.Emit("updateAvatar", data);
        }
    }

    IEnumerator UpdateWaitingMenu() {
        yield return new WaitForSeconds(1);
        Events.OnPlayersUpdate?.Invoke(GameManager.Instance.Players);
        Events.OnSessionChange?.Invoke(sessionId);
    }

    void accessData(JSONObject obj){
        switch(obj.type){
            case JSONObject.Type.OBJECT:
                for(int i = 0; i < obj.list.Count; i++){
                    string key = (string)obj.keys[i];
                    JSONObject j = (JSONObject)obj.list[i];
                    Debug.Log(key);
                    accessData(j);
                }
                break;
            case JSONObject.Type.ARRAY:
                foreach(JSONObject j in obj.list){
                    accessData(j);
                }
                break;
            case JSONObject.Type.STRING:
                Debug.Log(obj.str);
                break;
            case JSONObject.Type.NUMBER:
                Debug.Log(obj.n);
                break;
            case JSONObject.Type.BOOL:
                Debug.Log(obj.b);
                break;
            case JSONObject.Type.NULL:
                Debug.Log("NULL");
                break;
    
        }
    }

    public void NewSession (SocketIOEvent e) {
        e.data.GetField("sessionId", delegate(JSONObject data) {
            sessionId = data.str;
            Events.OnSessionChange?.Invoke(data.str);
            GameManager.Instance.Players.Add(new Player("host"));
            Debug.Log(sessionId);
        }, delegate(string name) {
            Debug.LogWarning("no game sessions");
        });
    }

    // Just host receive this event
    public void OnUpdateAvatar (SocketIOEvent e) {
        string id = e.data.list[0].str;
        string name = e.data.list[1].str;
        int avatar = (int) e.data.list[2].n;

        Debug.Log("new avatar update" + name);

        for (int i = 0; i < GameManager.Instance.Players.Count; i++) {
            if (GameManager.Instance.Players[i].id == id) {
                GameManager.Instance.Players[i].name = name;
                GameManager.Instance.Players[i].avatar = avatar;
            }
        }

        Events.OnPlayersUpdate?.Invoke(GameManager.Instance.Players);
        DispatchPlayers();
    }

    public void DispatchGameData(JSONObject data)
    {
        data.AddField("sessionId", sessionId);
        
        socket.Emit(UPDATE_GAMEDATA, data);
    }

    void OnUpdateGameData(SocketIOEvent e)
    {
        //if (_isHost) return;

        JSONObject cardContainer = e.data.GetField("cards");
        string[] cards = new string[cardContainer.list.Count];
        for (int i = 0; i < cardContainer.list.Count; i++)
        {
            cards[i] = cardContainer.list[i].GetField("card").str;
        }

        JSONObject orderContainer = e.data.GetField("turnOrder");
        string[] turnOrder = new string[orderContainer.list.Count];
        for(int i = 0; i < orderContainer.list.Count; i++)
        {
            turnOrder[i] = orderContainer.list[i].GetField("playerId").str;
        }

        GameManager.Instance.SetGameData(turnOrder, cards);
        //Events.OnGameCardsReceived?.Invoke(cards);
    }

    public void DispatchPlayers () {
        JSONObject data = new JSONObject(JSONObject.Type.OBJECT);
        data.AddField("sessionId", sessionId);
        JSONObject playersArray = new JSONObject(JSONObject.Type.ARRAY);

        for (int i = 0; i < GameManager.Instance.Players.Count; i++) {
            playersArray.Add(GameManager.Instance.Players[i].getJSON());
        }

        data.AddField("players", playersArray);
        socket.Emit(UPDATE_PLAYERS, data);
    }

    public void OnUpdatedPlayers (SocketIOEvent e) {
        if (_isHost) return;

        e.data.GetField("players", delegate(JSONObject obj) {
            GameManager.Instance.Players.Clear();

            foreach(JSONObject j in obj.list){
                Player p = new Player(j);
                p.isMe = _userId == p.id;
                GameManager.Instance.Players.Add(p);
            }

            Events.OnPlayersUpdate?.Invoke(GameManager.Instance.Players);
            Debug.Log("updated players list.");
        }, delegate(string name) {
            Debug.LogWarning("no players");
        });
    }

    public void Joinupdate (SocketIOEvent e) {
        Debug.Log("receive new join");

        if (GameManager.Instance.Players.Count < 8) {
            e.data.GetField("userId", delegate(JSONObject data) {
                GameManager.Instance.Players.Add(new Player(data.str));
                Debug.Log("new player in session: " + data.str);
            }, delegate(string name) {
                Debug.LogWarning("no player");
            });
        }
    }

    public void NewGameState (SocketIOEvent e) {

    }

    /*void Update() {   
        if (Input.GetKeyDown("space")){
            Events.OnNextPlayerTurn?.Invoke();
        }
        
        if (Input.GetKeyDown(KeyCode.Q)){
            Events.OnGameStart?.Invoke(new GameState());
        }

        if (Input.GetKeyDown(KeyCode.A)){
            Events.OnCheckHand?.Invoke();
        }

        if (Input.GetKeyDown(KeyCode.S)){
            Events.OnEmptyHand?.Invoke();
        }

        if (Input.GetKeyDown(KeyCode.D)){
            Events.OnFindHand?.Invoke();
        }
    }*/
}
