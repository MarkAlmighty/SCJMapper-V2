﻿using System;
using System.Collections.Generic;
using System.Xml;
using System.IO;
using System.Xml.Linq;
using SCJMapper_V2.Joystick;
using SCJMapper_V2.Gamepad;
using System.Linq;

namespace SCJMapper_V2.Options
{
  /// <summary>
  ///   Maintains an Deviceoptions - something like:
  ///
  ///	<deviceoptions name="Joystick - HOTAS Warthog">
  ///		<!-- Reduce the deadzone -->
  ///		<option input="x" deadzone="0.015" />
  ///		<option input="y" deadzone="0.015" />	
  ///		<option input="y" saturation="0.85" />	
  ///	</deviceoptions>	
  ///	
  /// [device] : set to device name (name shown in Windows Game Controllers control panel), currently known names follow
  ///	Joystick - HOTAS Warthog
  ///	Saitek X52 Pro Flight Controller
  ///	
  ///	</summary>
  public class Deviceoptions : CloneableDictionary<string, DeviceOptionParameter>
  {
    private static readonly log4net.ILog log = log4net.LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod( ).DeclaringType );


    #region Static parts

    // DevOptions GUI Slider properties - have them in one place only
    public const int DevOptSliderMax = 100;
    public const int DevOptSliderTick = DevOptSliderMax / 10;
    private const float DZ_scale = 500.0f; // Deadzone slider   00 .. 100 -> 0 .. 0.20 ( 0.002 scale)
    private const float SAT_scale = 200.0f;
    private const float SAT_offs = 1.0f - ( (float)DevOptSliderMax / SAT_scale ); // Saturation slider 00 .. 100 -> 0.5 .. 1.0 ( 0.005 scale)

    public static float DeadzoneFromSlider( int sliderValue )
    {
      return ( sliderValue / DZ_scale );
    }
    public static int DeadzoneToSlider( float value )
    {
      return (int)Math.Floor( value * DZ_scale );
    }
    public static int DeadzoneToSlider( string value )
    {
      return (int)Math.Floor( float.Parse( value ) * DZ_scale );
    }

    public static float SaturationFromSlider( int sliderValue )
    {
      return ( sliderValue / SAT_scale + SAT_offs );
    }
    public static int SaturationToSlider( float value )
    {
      return (int)Math.Floor( ( value - SAT_offs ) * SAT_scale );
    }
    public static int SaturationToSlider( string value )
    {
      return (int)Math.Floor( ( float.Parse( value ) - SAT_offs ) * SAT_scale );
    }



    private static char ID_Delimiter = '⁞';
    /// <summary>
    /// Create a DeviceOption ID from dev Name and the command
    /// </summary>
    /// <param name="devName">The game device name as retrieved from XInput</param>
    /// <param name="cmdCtrl">A device control that supports devOptions (all ananlog controls)</param>
    /// <returns></returns>
    public static string DevOptionID( string deviceClass, string devName, string cmdCtrl )
    {
      // cmdCtrl can be anything 
      //    v_flight_throttle_abs - js1_throttlez
      //    v_strafe_longitudinal - js1_y
      //    v_strafe_longitudinal - js1_roty
      //    v_strafe_longitudinal - xi1_shoulderl+thumbly
      //    v_strafe_longitudinal - xi1_thumbly

      string cmd = cmdCtrl.Trim( );
      // messy...
      if ( cmd.Contains( "throttle" ) ) cmd = cmd.Replace( "throttle", "" ); // this is not suitable for the devOption
      if ( cmd.Contains( "_" ) ) {
        int l = cmd.LastIndexOf( "_" );
        cmd = cmd.Substring( l + 1 ); // assuming it is never the last one..
      }
      if ( cmd.Contains( "+" ) ) {
        int l = cmd.LastIndexOf( "+" );
        cmd = cmd.Substring( l + 1 ); // assuming it is never the last one..
      }
      //   we have to trick the gamepad name here to CIG generic
      return string.Format( "{0}{1}{2}", ( GamepadCls.IsDeviceClass( deviceClass ) ) ? GamepadCls.DevNameCIG : devName, ID_Delimiter, cmd );
    }

    #endregion


    private List<string> m_stringOptions = new List<string>( ); // collected options from XML that are not parsed



    /// <summary>
    /// Clone this object
    /// </summary>
    /// <returns>A deep Clone of this object</returns>
    public new object Clone()
    {
      var dop = new Deviceoptions( (CloneableDictionary<string, DeviceOptionParameter>)base.Clone( ) );
      // more objects to deep copy
      dop.m_stringOptions = new List<string>( m_stringOptions );

#if DEBUG
      // check cloned item
      System.Diagnostics.Debug.Assert( CheckClone( dop ) );
#endif
      return dop;
    }

#if DEBUG
    /// <summary>
    /// Check clone against This
    /// </summary>
    /// <param name="clone"></param>
    /// <returns>True if the clone is identical but not a shallow copy</returns>
    private bool CheckClone( Deviceoptions clone )
    {
      bool ret = true;
      // object vars first
      ret &= ( !object.ReferenceEquals( this.m_stringOptions, clone.m_stringOptions ) );  // shall not be the same object !!

      // check THIS Dictionary
      ret &= ( this.Count == clone.Count );
      if ( ret ) {
        for ( int i = 0; i < this.Count; i++ ) {
          ret &= ( this.ElementAt( i ).Key == clone.ElementAt( i ).Key );

          ret &= ( !object.ReferenceEquals( this.ElementAt( i ).Value, clone.ElementAt( i ).Value ) );  // shall not be the same object !!
          ret &= ( this.ElementAt( i ).Value.CheckClone( clone.ElementAt( i ).Value ) ); // sub check
        }
      }
      return ret;
    }
#endif



    private Deviceoptions( CloneableDictionary<string, DeviceOptionParameter> init )
    {
      foreach ( KeyValuePair<string, DeviceOptionParameter> kvp in init ) {
        this.Add( kvp.Key, kvp.Value );
      }
    }


    // ctor
    public Deviceoptions()
    {
      // create all devOptions for all devices found (they may or may no be used)
      foreach ( JoystickCls js in DeviceInst.JoystickListRef ) {
        foreach ( string input in js.AnalogCommands ) {
          string doid = DevOptionID( JoystickCls.DeviceClass, js.DevName, input );
          if ( !this.ContainsKey( doid ) ) {
            this.Add( doid, new DeviceOptionParameter( js, input, "", "" ) ); // init with disabled defaults
          }
          else {
            log.WarnFormat( "cTor - Joystick DO_ID {0} exists (likely a duplicate device name e,g, vJoy ??)", doid );
          }
        }
      }

      // add gamepad if there is any
      if ( DeviceInst.GamepadRef != null ) {
        foreach ( string input in DeviceInst.GamepadRef.AnalogCommands ) {
          string doid = DevOptionID( GamepadCls.DeviceClass, DeviceInst.GamepadRef.DevName, input );
          if ( !this.ContainsKey( doid ) ) {
            this.Add( doid, new DeviceOptionParameter( DeviceInst.GamepadRef, input, "", "" ) ); // init with disabled defaults
          }
          else {
            log.WarnFormat( "cTor - Gamepad DO_ID {0} exists", doid );
          }
        }
      }
    }


    new public int Count
    {
      get { return ( m_stringOptions.Count + base.Count ); }
    }

    /// <summary>
    /// Reset all items that will be assigned dynamically while scanning the actions
    /// - currently Action
    /// </summary>
    public void ResetDynamicItems()
    {
      foreach ( KeyValuePair<string, DeviceOptionParameter> kv in this ) {
        kv.Value.Action = "";
      }
    }


    /// <summary>
    /// Rounds a string to 3 decimals (if it is a number..)
    /// </summary>
    /// <param name="valString">A value string</param>
    /// <returns>A rounded value string - or the string if not a number</returns>
    private string RoundString( string valString )
    {
      double d = 0;
      if ( ( !string.IsNullOrEmpty( valString ) ) && double.TryParse( valString, out d ) ) {
        return d.ToString( "0.000" );
      }
      else {
        return valString;
      }
    }


    private string[] FormatXml( string xml )
    {
      try {
        XDocument doc = XDocument.Parse( xml );
        return doc.ToString( ).Split( new string[] { string.Format( "\n" ) }, StringSplitOptions.RemoveEmptyEntries );
      } catch ( Exception ) {
        return new string[] { xml };
      }
    }

    /// <summary>
    /// Dump the Deviceoptions as partial XML nicely formatted
    /// </summary>
    /// <returns>the action as XML fragment</returns>
    public string toXML()
    {
      string r = "";

      // and dump the contents of plain string options
      foreach ( string x in m_stringOptions ) {

        if ( !string.IsNullOrWhiteSpace( x ) ) {
          foreach ( string line in FormatXml( x ) ) {
            r += string.Format( "\t{0}", line );
          }
        }

        r += string.Format( "\n" );
      }

      // dump Tuning 
      foreach ( KeyValuePair<string, DeviceOptionParameter> kv in this ) {
        r += kv.Value.Deviceoptions_toXML( );
      }

      return r;
    }



    /// <summary>
    /// Read an Deviceoptions from XML - do some sanity check
    /// </summary>
    /// <param name="xml">the XML action fragment</param>
    /// <returns>True if an action was decoded</returns>
    public Boolean fromXML( string xml )
    {
      /* 
       * This can be a lot of the following options
       * try to do our best....
       * 
       * 	<deviceoptions name="Joystick - HOTAS Warthog">
            <!-- Reduce the deadzone -->
            <option input="x" deadzone="0.015" />
            <option input="y" deadzone="0.015" />	
          </deviceoptions>
       * 
      */

      XmlReaderSettings settings = new XmlReaderSettings( );
      settings.ConformanceLevel = ConformanceLevel.Fragment;
      settings.IgnoreWhitespace = true;
      settings.IgnoreComments = true;
      XmlReader reader = XmlReader.Create( new StringReader( xml ), settings );

      reader.Read( );

      string name = "";

      if ( reader.HasAttributes ) {
        name = reader["name"];
        string devClass = ( name == GamepadCls.DevNameCIG ) ? GamepadCls.DeviceClass : JoystickCls.DeviceClass;// have to trick this one...

        reader.Read( );
        // try to disassemble the items
        while ( !reader.EOF ) {
          if ( reader.Name.ToLowerInvariant( ) == "option" ) {
            if ( reader.HasAttributes ) {
              string input = reader["input"];
              string deadzone = RoundString( reader["deadzone"] );
              string saturation = RoundString( reader["saturation"] );
              if ( !string.IsNullOrWhiteSpace( input ) ) {
                string doID = "";
                doID = DevOptionID( devClass, name, input );
                if ( !string.IsNullOrWhiteSpace( deadzone ) ) {
                  float testF;
                  if ( !float.TryParse( deadzone, out testF ) ) { // check for valid number in string
                    deadzone = "0.000";
                  }
                  if ( !this.ContainsKey( doID ) ) {
                    log.InfoFormat( "Cannot caputre Device Options for device <{0}> - unknown device!", name );
                    //this.Add( doID, new DeviceOptionParameter( devClass, name, input, deadzone, saturation ) );
                  }
                  else {
                    // add deadzone value tp existing
                    this[doID].DeadzoneUsed = true;
                    this[doID].Deadzone = deadzone;
                  }
                }
                if ( !string.IsNullOrWhiteSpace( saturation ) ) {
                  float testF;
                  if ( !float.TryParse( saturation, out testF ) ) { // check for valid number in string
                    saturation = "1.000";
                  }
                  if ( !this.ContainsKey( doID ) ) {
                    log.InfoFormat( "Cannot caputre Device Options for device <{0}> - unknown device!", name );
                    //this.Add( doID, new DeviceOptionParameter( devClass, name, input, deadzone, saturation ) ); // actually not supported..
                  }
                  else {
                    // add saturation value tp existing
                    this[doID].SaturationUsed = true;
                    this[doID].Saturation = saturation;
                  }
                }
              }
              else {
                //? option node has not the needed attributes
                log.ErrorFormat( "Deviceoptions.fromXML: option node has not the needed attributes" );
              }
            }
            else {
              //?? option node has NO attributes
              log.ErrorFormat( "Deviceoptions.fromXML: option node has NO attributes" );
            }
          }

          reader.Read( );
        }//while
      }
      else {
        //??
        if ( !m_stringOptions.Contains( xml ) ) m_stringOptions.Add( xml );
      }

      return true;
    }


  }
}
