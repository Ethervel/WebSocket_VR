# Script List - WebSocket VR Project

## Network (Assets/Scrips/Network/)
| Script | Description |
|--------|-------------|
| `VRNetworkManager.cs` | WebSocket connection, message routing, peer management |
| `VRRoomManager.cs` | Room lifecycle (create, join, leave), player tracking |
| `VRGameManager.cs` | Player spawning, local/remote player management, teleportation |

## VR (Assets/Scrips/VR/)
| Script | Description |
|--------|-------------|
| `BootstrapManager.cs` | App initialization, XR setup, scene loading |
| `VRPlayerController.cs` | VR player movement, hand tracking, input handling |
| `TeleportOnButtonClick.cs` | Teleport to room on UI button click |
| `TeleportOnGrab.cs` | Teleport triggered by grab interaction |
| `ControllerInputFix.cs` | XR controller input fixes |
| `ControllerTrackingFix.cs` | Controller tracking issues fix |
| `ControllerModelLoader.cs` | Dynamic controller model loading |
| `VRTrackingFix.cs` | VR tracking calibration |
| `XRUIInteractionBridge.cs` | Bridge between XR and UI systems |
| `XRInteractorInputBridge.cs` | XR interactor input handling |

## Desktop (Assets/Scrips/Desktop/)
| Script | Description |
|--------|-------------|
| `DesktopPlayerController.cs` | Desktop player movement (WASD), mouse look, interactions |

## WebRTC (Assets/Scrips/WebRTC/)
| Script | Description |
|--------|-------------|
| `VoiceChatManager.cs` | Voice chat orchestration, peer connections |
| `WebRTCPeerManager.cs` | Individual WebRTC peer connection management |
| `MicrophoneManager.cs` | Microphone input, audio capture |
| `RemoteAudioManager.cs` | Remote player audio playback |
| `WebRTCSignaling.cs` | WebRTC signaling (offer/answer/ICE) |
| `WebRTCConfiguration.cs` | STUN/TURN server configuration |
| `VoiceChatData.cs` | Voice chat data structures |

## WhiteBoard (Assets/Scrips/WhiteBoard/)
| Script | Description |
|--------|-------------|
| `Whiteboard.cs` | Main whiteboard controller, presentation mode |
| `WhiteboardDrawingSurface.cs` | Network drawing surface (receives strokes) |
| `WhiteboardMarker.cs` | VR marker tool for drawing |
| `WhiteboardEraser.cs` | VR eraser tool |
| `DesktopWhiteboardDrawer.cs` | Desktop mouse drawing |
| `WhiteboardBarUI.cs` | Whiteboard toolbar UI |
| `WhiteboardUIManager.cs` | Whiteboard UI management |
| `WhiteboardUIHelper.cs` | UI helper functions |
| `WhiteboardUISetup.cs` | UI setup utilities |
| `WhiteboardNetworkData.cs` | Network data structures |

## Sharing (Assets/Scrips/Sharing/)
| Script | Description |
|--------|-------------|
| `ScreenShareManager.cs` | Screen sharing (capture, encode, broadcast) |
| `FileShareManager.cs` | File sharing between clients |
| `FilePresentationManager.cs` | File presentation on whiteboard |
| `ScreenShareData.cs` | Screen share data structures |
| `FileShareData.cs` | File share data structures |
| `FilePresentationData.cs` | Presentation data structures |
| `WindowCapture.cs` | Windows screen capture |

## Interaction (Assets/Scrips/Interaction/)
| Script | Description |
|--------|-------------|
| `LaserPointer.cs` | VR/Desktop laser pointer |
| `LaserPointerData.cs` | Laser pointer network data |
| `RoomBlocker.cs` | Blocks passage until player joins room |

## Avatar (Assets/Scrips/Avatar/)
| Script | Description |
|--------|-------------|
| `AvatarCustomization.cs` | Avatar color/appearance customization |
| `AvatarColorTarget.cs` | Applies avatar color to renderers |

## Auth (Assets/Scrips/Auth/)
| Script | Description |
|--------|-------------|
| `AuthManager.cs` | Authentication (login, register, token management) |
| `AuthUI.cs` | Login/Register UI panels |

## Recording (Assets/Scrips/Recording/)
| Script | Description |
|--------|-------------|
| `RecordingManager.cs` | Recording orchestration, pipeline management |
| `SpectatorCameraController.cs` | Spectator camera for recording |
| `FFmpegEncoder.cs` | FFmpeg video encoding |
| `AudioCapture.cs` | Audio capture for recording |
| `RecordingData.cs` | Recording settings and data structures |
| `RecordingTestHelper.cs` | Recording test utilities |

## Audio (Assets/Scrips/Audio/)
| Script | Description |
|--------|-------------|
| `SoundManager.cs` | Global sound management |
| `SoundManagerIntegration.cs` | Sound manager integration helpers |
| `AmbienceManager.cs` | Ambient sound management |
| `AudioMuteZone.cs` | Zone-based audio muting |
| `UIButtonSounds.cs` | UI button click sounds |

## UI (Assets/Scrips/UI/)
| Script | Description |
|--------|-------------|
| `LaunchLoadingScreen.cs` | Initial loading screen with progress |
| `VRCanvasAdapter.cs` | Adapts Canvas for VR (World Space) |
| `VRMenuUi.cs` | VR menu system |
| `VRMenuToggle.cs` | VR menu toggle button |
| `VRMenuSidebar.cs` | VR menu sidebar navigation |
| `VRMenuCloseButton.cs` | VR menu close button |
| `VRMenuUISetup.cs` | VR menu setup utilities |
| `VRFollowMenu.cs` | Menu that follows VR camera |
| `VRFileBrowser.cs` | VR file browser |
| `FileSharingUI.cs` | File sharing UI |
| `FileShareUISetup.cs` | File share UI setup |
| `FilePresentationUI.cs` | File presentation UI |
| `VoiceChatUI.cs` | Voice chat status UI |
| `GlobalKeyboardAutoBind.cs` | Global keyboard input binding |

## UI/Menu (Assets/Scrips/UI/Menu/)
| Script | Description |
|--------|-------------|
| `VRMenuPageRoom.cs` | Room management page |
| `VRMenuPageAvatar.cs` | Avatar customization page |
| `VRMenuPageSettings.cs` | Settings page |
| `VRMenuPageVoice.cs` | Voice chat settings page |
| `VRMenuPageRecording.cs` | Recording controls page |
| `VRMenuExitDialog.cs` | Exit confirmation dialog |

## UI/MainMenu (Assets/Scrips/UI/MainMenu/)
| Script | Description |
|--------|-------------|
| `MainMenuManager.cs` | Main menu orchestration |
| `MainMenuSettings.cs` | Settings persistence |
| `MainMenuOptionsUI.cs` | Options/settings UI |

## Debug (Assets/Scrips/Debug/)
| Script | Description |
|--------|-------------|
| `DebugManager.cs` | Debug logging with categories |
| `XRDebugOverlay.cs` | XR debug information overlay |

## Utils (Assets/Scrips/Utils/)
| Script | Description |
|--------|-------------|
| `JsonHelper.cs` | JSON serialization helpers |
| `TransformUtility.cs` | Transform utility functions |
| `SceneLoader.cs` | Scene transitions with fade, additive loading |
| `ScreenFader.cs` | Fade effect (UI Image + VR sphere) |
| `LoadingIndicator.cs` | Loading spinner with progress bar |

## Effects (Assets/Scrips/Effects/)
| Script | Description |
|--------|-------------|
| `GlowingLight.cs` | Glowing light effect |

## Testing (Assets/Scrips/Testing/)
| Script | Description |
|--------|-------------|
| `VRNetworkedInteractable.cs` | Test networked interactable objects |

---
*Last updated: 2026-02-25*
*Total scripts: 85*
