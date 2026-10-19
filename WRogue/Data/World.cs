using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace djack.RogueSurvivor.Data
{
    [Serializable]
    class World
    {
        #region Fields
        District[,] m_DistrictsGrid;
        int m_Size;
        Weather m_Weather;
        int m_NextWeatherCheckTurn;  // alpha10
        [System.Runtime.Serialization.OptionalField] List<WeatherChange> weatherHistory;
        [Serializable] sealed class WeatherChange
        { internal int Turn; internal Weather Weather; }
        #endregion

        #region Properties
        public int Size
        {
            get { return m_Size; }
        }

        public District this[int x, int y]
        {
            get 
            {
                if (x < 0 || x >= m_Size)
                    throw new ArgumentOutOfRangeException("x");
                if (y < 0 || y >= m_Size)
                    throw new ArgumentOutOfRangeException("y");
                return m_DistrictsGrid[x, y]; 
            }
            set
            {
                if (x < 0 || x >= m_Size)
                    throw new ArgumentOutOfRangeException("x");
                if (y < 0 || y >= m_Size)
                    throw new ArgumentOutOfRangeException("y");
                m_DistrictsGrid[x, y] = value;
            }
        }

        public Weather Weather
        {
            get { return Engine.SimulationClock.WeatherAt(this, m_Weather); }
            set { m_Weather = value; }
        }

        public int NextWeatherCheckTurn
        {
            get { return m_NextWeatherCheckTurn; }
            set { m_NextWeatherCheckTurn = value; }
        }
        #endregion

        internal void EnsureWeatherHistory()
        {
            if (weatherHistory == null) weatherHistory = new List<WeatherChange> {
                new WeatherChange { Turn = 0, Weather = m_Weather } };
        }
        internal void RecordWeather(int turn)
        {
            EnsureWeatherHistory();
            int last = weatherHistory.Count - 1;
            if (turn < weatherHistory[last].Turn) throw new InvalidOperationException("Weather history cannot run backwards.");
            if (weatherHistory[last].Weather != m_Weather)
                weatherHistory.Add(new WeatherChange { Turn = turn, Weather = m_Weather });
        }
        internal Weather WeatherAt(int turn)
        {
            if (weatherHistory == null) return m_Weather;
            for (int i = weatherHistory.Count - 1; i >= 0; i--)
                if (weatherHistory[i].Turn <= turn) return weatherHistory[i].Weather;
            return weatherHistory[0].Weather;
        }
        internal int NextWeatherChange(int turn)
        {
            if (weatherHistory != null) foreach (var change in weatherHistory)
                if (change.Turn > turn) return change.Turn;
            return Int32.MaxValue;
        }

        #region Init
        public World(int size)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException("size <=0");

            m_DistrictsGrid = new District[size, size];
            m_Size = size;
            m_Weather = Weather.CLEAR;
            m_NextWeatherCheckTurn = 0;
        }
        #endregion

        #region Coordinates
        /// <summary>
        /// Trim district coordinates to world bounds.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public void TrimToBounds(ref int x, ref int y)
        {
            if (x < 0) x = 0;
            if (x >= m_Size) x = m_Size - 1;
            if (y < 0) y = 0;
            if (y >= m_Size) y = m_Size - 1;
        }

        /// <summary>
        /// District coordinates to string.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns>[A-Z][0-9]</returns>
        public static string CoordToString(int x, int y)
        {
            return String.Format("{0}{1}", (char)('A'+x), y);
        }
        #endregion

        #region Pre-saving
        public void OptimizeBeforeSaving()
        {
            for (int x = 0; x < m_Size; x++)
                for (int y = 0; y < m_Size; y++)
                    m_DistrictsGrid[x, y].OptimizeBeforeSaving();
        }
        #endregion
    }
}
