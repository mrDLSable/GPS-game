﻿using JetBrains.Annotations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

[Serializable]
public class WorldManager : MonoBehaviour
{
    public GPSManager GPSManager;
    public Camera camera;
    public GameObject player;
    public GameObject zonePrefab;
    public GameObject pointPrefab;
    public GameObject currentZoneMarker;
    public TMPro.TextMeshProUGUI scoreLabel;

    public TMPro.TextMeshProUGUI zonesLabel;

    private ZoneID playerZone;
    private static ZoneID originZone;

    public const int scalar = 10000;
    public const int zoomLevel = 18;
    public const int zoneSize = 10;

    private bool forceOnEnter = false;
    private bool centerCameraOnPlayer = true;

    private Dictionary<ZoneID, ZoneData> zones = new Dictionary<ZoneID, ZoneData>();
    private List<GPSPoint> points = new List<GPSPoint>();

    public static int Distance = 0;
    public static DateTime LastDistanceCalc = DateTime.Now;

    public static int MaxZones = 500;

    // Start is called before the first frame update
    void Start()
    {
        LoadWorld();
        forceOnEnter = true;
    }

    // Update is called once per frame
    void Update()
    {
        foreach(ZoneData zoneData in zones.Values)
        {
            zoneData.Update(player.transform.position);
        }

        if (GPSManager.IsReady)
        {
            ZoneID zoneID = GetZoneID(GPSManager.position);

            if (zoneID != playerZone || forceOnEnter)
            {
                playerZone = zoneID;

                if (!PosHasZone(GPSManager.position))
                {
                    ZoneData zone = new ZoneData(zoneID);
                    zone.SetActive(true, originZone);
                    zones.Add(zoneID, zone);
                    forceOnEnter = true;
                }

                EnterZone(zoneID, forceOnEnter);

                if (forceOnEnter)
                {
                    RepositionPlayer(zoneID);
                    CenterCameraOnPlayer(player.transform.position);
                }

                forceOnEnter = false;
            }
            else
            {
                RepositionPlayer(zoneID);
            }

            //Track position changes
            UpdatePoints();
        }
    }

    /// <summary>
    /// Update the GPS points situation.
    /// </summary>
    public void UpdatePoints()
    {
        if (points.Count == 0)
        {
            CreatePoint(GPSManager.position, DateTime.Now, -1f);
        }
        else if (GetDistance(points[points.Count - 1].GetGPSPosition(), GPSManager.position) > 10.0f)
        {
            CreatePoint(GPSManager.position, DateTime.Now, -1f);
        }
    }

    /// <summary>
    /// Set the maximum number of zones to be rendered
    /// </summary>
    /// <param name="amount">The maximum amount</param>
    public void SetMaxZones(int amount)
    {
        if(amount > 0 && amount <= 1000)
        {
            MaxZones = amount;
            EnterZone(playerZone, true);
        }
    }

    /// <summary>
    /// Create a new point and add it to the list.
    /// </summary>
    /// <param name="GPSposition"></param>
    /// <param name="dateTime"></param>
    public void CreatePoint(Vector2 GPSposition, DateTime dateTime, float actualDistance)
    {
        GPSPoint newPoint = new GPSPoint(GPSposition, dateTime);
        newPoint.Reposition();

        if(points.Count > 0)
        {
            if (actualDistance == -1f)
            {
                newPoint.actualDistance = GetDistance(newPoint.GetGPSPosition(), points[points.Count - 1].GetGPSPosition());
            }
            else
            {
                newPoint.actualDistance = actualDistance;
            }
            points[points.Count - 1].SetNext(newPoint);
        }
        points.Add(newPoint);
        TrunkatePoints();
    }

    /// <summary>
    /// Checks to see if the last two points can be combined into one.
    /// </summary>
    private void TrunkatePoints()
    {
        if(points.Count >= 3)
        {
            float totalActualDistance = points[points.Count - 1].actualDistance + points[points.Count - 2].actualDistance;
            if(totalActualDistance <= 100f)
            {
                float onMapDistance = GetDistance(points[points.Count - 3].GetGPSPosition(), points[points.Count - 1].GetGPSPosition());
                if(onMapDistance <= totalActualDistance && onMapDistance >= totalActualDistance * 0.95f)
                {
                    points[points.Count - 3].SetNext(points[points.Count - 1]);
                    points[points.Count - 1].actualDistance = totalActualDistance;
                    points[points.Count - 3].DrawLine();
                    points[points.Count - 2].DestroyGameObject();
                    points.RemoveAt(points.Count - 2);
                }
            }
        }
    }

    /// <summary>
    /// Saves the world data.
    /// </summary>
    private void SaveWorld()
    {
        print("saving world");
        BinaryFormatter bf = new BinaryFormatter();
        FileStream file = File.Create(Application.persistentDataPath + "/gamesave.save");
        bf.Serialize(file, zones);
        file.Close();
        file = File.Create(Application.persistentDataPath + "/points.save");
        bf.Serialize(file, points);
        file.Close();
    }

    /// <summary>
    /// Load the save files
    /// </summary>
    private void LoadWorld()
    {
        if(File.Exists(Application.persistentDataPath + "/gamesave.save"))
        {
            print("Loading world");
            BinaryFormatter bf = new BinaryFormatter();

            //Load zones
            FileStream file = File.Open(Application.persistentDataPath + "/gamesave.save", FileMode.Open);
            zones = (Dictionary<ZoneID, ZoneData>)bf.Deserialize(file);
            file.Close();

            //Load points
            file = File.Open(Application.persistentDataPath + "/points.save", FileMode.Open);
            List<GPSPoint> tempPoints = (List<GPSPoint>)bf.Deserialize(file);
            file.Close();
            LoadPoints(tempPoints);

            RepositionWorld();
        }
    }

    /// <summary>
    /// Load a given list of points into the game.
    /// </summary>
    /// <param name="inputPoints">the given list of points</param>
    private void LoadPoints(List<GPSPoint> inputPoints)
    {
        foreach (GPSPoint point in inputPoints)
        {
            CreatePoint(point.GetGPSPosition(), point.dateTime, point.actualDistance);
            if(point.dateTime.Date != DateTime.Today)
            {
                points[points.Count - 1].DestroyGameObject();
            }
        }
    }

    /// <summary>
    /// Generates the image ID based on the given GPS coords
    /// </summary>
    /// <param name="coords">GPS coords</param>
    /// <returns>Image ID</returns>
    private ZoneID GetZoneID(Vector2 coords)
    {
        int x = long2tilex(coords.x, zoomLevel);
        int y = lat2tiley(coords.y, zoomLevel);
        return new ZoneID(x, y);
    }

    /// <summary>
    /// Checks to see if given GPS coords have a zone.
    /// </summary>
    /// <param name="coords">GPS coords</param>
    /// <returns>true=has a zone</returns>
    bool PosHasZone(Vector2 coords)
    {
        ZoneID imageID = GetZoneID(coords);
        if (zones.ContainsKey(imageID))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Saves the texture into a zonedata object to be saved.
    /// </summary>
    /// <param name="zoneID">The zone in which the texture has to be saved.</param>
    /// <param name="texture">The texture to be saved.</param>
    public void SaveTexture(ZoneID zoneID, Texture2D texture)
    {
        if (zones.ContainsKey(zoneID))
        {
            zones[zoneID].SaveTexture(texture, zoneID);
        }
    }

    public int CalculateScore()
    {
        int score = 0;

        foreach(ZoneData zoneData in zones.Values)
        {
            score += zoneData.points;
        }

        return score;
    }

    /// <summary>
    /// Gets called every time a player enters a zone and at game launch.
    /// </summary>
    /// <param name="zoneID">THe zoneID of zone the player entered.</param>
    private void EnterZone(ZoneID zoneID, bool forceReposition)
    {
        zones[zoneID].OnEnter();
        scoreLabel.text = "Score: " + CalculateScore();
        zonesLabel.text = "Zones: " + zones.Count;

        int lineCount = 0;
        int maxLines = 4;
        int zoneCount = 0;
        float closestHighestMaxDistance = 20f;
        ZoneID closestHigestZone = default;
        int closestHigestScore = 0;
        foreach (KeyValuePair<ZoneID, ZoneData> zone in zones.OrderBy(key => key.Value.GetDistanceFromPlayer(playerZone)))
        {
            //Calculate the closest highest zone.
            if(zone.Value.points > closestHigestScore
                && zone.Value.CanVisit()
                && zone.Value.GetDistanceFromPlayer(playerZone) <= closestHighestMaxDistance)
            {
                closestHigestScore = zone.Value.points;
                closestHigestZone = zone.Key;
            }
            else if(closestHigestScore == zone.Value.points
                && zone.Value.GetDistanceFromPlayer(playerZone) < zones[closestHigestZone].GetDistanceFromPlayer(playerZone)
                && zone.Value.CanVisit()
                && zone.Value.GetDistanceFromPlayer(playerZone) <= closestHighestMaxDistance)
            {
                closestHigestScore = zone.Value.points;
                closestHigestZone = zone.Key;
            }

            if(zoneCount < MaxZones || lineCount < maxLines)
            {
                zoneCount++;
                zone.Value.SetActive(true, originZone);

                if (zone.Value.CanVisit() && lineCount < maxLines)
                {
                    zone.Value.lineActive = true;
                    zone.Value.lineColor = UnityEngine.Color.green;
                    lineCount++;
                }
                else
                {
                    zone.Value.lineActive = false;
                }
            }
            else
            {
                zone.Value.SetActive(false, originZone);
            }
        }

        //Reposition zones if the player gets too far from the origin
        int recalcAMount = 1000;
        if (player.transform.position.x >= recalcAMount || player.transform.position.z >= recalcAMount || forceReposition)
        {
            RepositionWorld();
        }

        //Activate closest highest zone.
        if (closestHigestZone != default)
        {
            zones[closestHigestZone].lineActive = true;
            zones[closestHigestZone].lineColor = UnityEngine.Color.blue;
            zones[closestHigestZone].SetActive(true, originZone);
            zones[closestHigestZone].Update(player.transform.position);
        }

        currentZoneMarker.transform.position = zones[zoneID].GetGameObject().transform.position;

        SaveWorld();
    }

    /// <summary>
    /// Repositions the zones around the position of the player.
    /// </summary>
    private void RepositionWorld()
    {
        Debug.Log("Reposition");
        originZone = playerZone;
        foreach (ZoneData zoneData in zones.Values)
        {
            zoneData.reposition(playerZone, zoneSize);
        }

        foreach (GPSPoint point in points)
        {
            point.Reposition();
        }

        foreach (GPSPoint point in points)
        {
            point.DrawLine();
        }
    }

    private void CenterCameraOnPlayer(Vector3 playerPos)
    {
        Vector3 camPos = camera.transform.position;
        camPos.x = playerPos.x;
        camPos.z = playerPos.z;
        camera.transform.position = camPos;
    }

    private void RepositionPlayer(ZoneID zoneID)
    {
        //Interpolate position in zone and move player to said position.
        Vector2 gpsLocation = GPSManager.position;
        Vector2 thisZoneStart = new Vector2(tilex2long(zoneID.x, zoomLevel), tiley2lat(zoneID.y, zoomLevel));
        Vector2 nextZoneStartX = new Vector2(tilex2long(zoneID.x + 1, zoomLevel), tiley2lat(zoneID.y, zoomLevel));
        Vector2 nextZoneStartY = new Vector2(tilex2long(zoneID.x, zoomLevel), tiley2lat(zoneID.y + 1, zoomLevel));
        float percentageX = (gpsLocation.x - thisZoneStart.x) / (nextZoneStartX.x - thisZoneStart.x);
        float percentageY = (gpsLocation.y - thisZoneStart.y) / (nextZoneStartY.y - thisZoneStart.y);
        if (percentageY < 0) percentageY = 0;
        if (percentageX < 0) percentageX = 0;

        Vector3 playerPos = player.transform.position;
        playerPos.x = zones[zoneID].GetGameObject().transform.position.x + percentageX * zoneSize - 0.5f * zoneSize;
        playerPos.z = zones[zoneID].GetGameObject().transform.position.z + -1 * percentageY * zoneSize + 0.5f * zoneSize;
        player.transform.position = playerPos;

        if (centerCameraOnPlayer)
        {
            CenterCameraOnPlayer(playerPos);
        }
    }

    /// <summary>
    /// Converts a given GPS position into their respective gamespace coordinates
    /// </summary>
    /// <param name="GPSPos">GPS position (x = lon, y = lat)</param>
    /// <returns>Gamespace coordinates</returns>
    public static Vector2 GPSToGameCoords(Vector2 GPSPos)
    {
        ZoneID zoneID = new ZoneID(long2tilex(GPSPos.x, zoomLevel), lat2tiley(GPSPos.y, zoomLevel));
        Vector2 thisZoneStart = new Vector2(tilex2long(zoneID.x, zoomLevel), tiley2lat(zoneID.y, zoomLevel));
        Vector2 nextZoneStartX = new Vector2(tilex2long(zoneID.x + 1, zoomLevel), tiley2lat(zoneID.y, zoomLevel));
        Vector2 nextZoneStartY = new Vector2(tilex2long(zoneID.x, zoomLevel), tiley2lat(zoneID.y + 1, zoomLevel));
        float percentageX = (GPSPos.x - thisZoneStart.x) / (nextZoneStartX.x - thisZoneStart.x);
        float percentageY = (GPSPos.y - thisZoneStart.y) / (nextZoneStartY.y - thisZoneStart.y);
        if (percentageY < 0) percentageY = 0;
        if (percentageX < 0) percentageX = 0;

        float x = (zoneID.x - originZone.x + percentageX - 0.5f) * zoneSize;
        float y = (zoneID.y - originZone.y + percentageY - 0.5f) * zoneSize * -1;
        return new Vector2(x, y);
    }

    public void OnCenterButtonClick()
    {
        centerCameraOnPlayer = !centerCameraOnPlayer;
    }

    public ZoneData GetZoneData(ZoneID zoneID)
    {
        if (zones.ContainsKey(zoneID))
        {
            return zones[zoneID];
        }
        return null;
    }

    public static int long2tilex(float lon, int z)
    {
        return (int)Mathf.Floor((lon + 180.0f) / 360.0f * (1 << z));
    }

    public static int lat2tiley(float lat, int z)
    {
        return (int)Mathf.Floor((1 - Mathf.Log(Mathf.Tan(Mathf.Deg2Rad * lat) + 1 / Mathf.Cos(Mathf.Deg2Rad * lat)) / Mathf.PI) / 2 * (1 << z));
    }

    public static float tilex2long(int x, int z)
    {
        return x / (float)(1 << z) * 360.0f - 180;
    }

    public static float tiley2lat(int y, int z)
    {
        float n = Mathf.PI - 2.0f * Mathf.PI * y / (float)(1 << z);
        return 180.0f / Mathf.PI * Mathf.Atan(0.5f * (Mathf.Exp(n) - Mathf.Exp(-n)));
    }

    /// <summary>
    /// Calculates the distance between two given gps coordinates
    /// </summary>
    /// <param name="pos1">coordinate1</param>
    /// <param name="pos2">coordinate2</param>
    /// <returns>distance in meters</returns>
    public float GetDistance(Vector2 pos1, Vector2 pos2)
    {
        var d1 = pos1.y * (Mathf.PI / 180.0f);
        var num1 = pos1.x * (Mathf.PI / 180.0f);
        var d2 = pos2.y * (Mathf.PI / 180.0f);
        var num2 = pos2.x * (Mathf.PI / 180.0f) - num1;
        var d3 = Mathf.Pow(Mathf.Sin((d2 - d1) / 2.0f), 2.0f) + Mathf.Cos(d1) * Mathf.Cos(d2) * Mathf.Pow(Mathf.Sin(num2 / 2.0f), 2.0f);

        return 6376500.0f * (2.0f * Mathf.Atan2(Mathf.Sqrt(d3), Mathf.Sqrt(1.0f - d3)));
    }

    /// <summary>
    /// Calculate the total distance traveled today
    /// </summary>
    /// <returns>distance in meters</returns>
    public static int GetDistanceToday()
    {
        if(LastDistanceCalc.AddSeconds(10) > DateTime.Now)
        {
            return Distance;
        }

        WorldManager worldManager = GameObject.FindGameObjectWithTag("GameManager").GetComponent<WorldManager>();

        float tempDistance = 0f;

        foreach (GPSPoint point in worldManager.points)
        {
            if (point.HasGameObject())
            {
                tempDistance += point.actualDistance;
            }
        }

        Distance = Mathf.FloorToInt(tempDistance);
        LastDistanceCalc = DateTime.Now;
        return Distance;
    }
}
