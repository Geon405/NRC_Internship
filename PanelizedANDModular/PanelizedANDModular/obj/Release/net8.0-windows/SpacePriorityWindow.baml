   M S B A M L   `   `   `  ����   PanelizedAndModularFinal2    ,PanelizedAndModularFinal.SpacePriorityWindow   W SPresentationCore, Version=8.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35\ XPresentationFramework, Version=8.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35e aSystem.Windows.Controls.Ribbon, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089R NWindowsBase, Version=8.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35F 9http://schemas.microsoft.com/winfx/2006/xaml/presentation     R NSystem.Xaml, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089:x,http://schemas.microsoft.com/winfx/2006/xaml       � Title$  Assign Space Priorities��$	��350��$	��400��.���  � RowDefinitions �� $��* ��� $
��Auto �.+�&   System.Windows.Controls.DataGrid  -     Name$ PrioritiesDataGrid��   AutoGenerateColumns$ False��$��10q�   Columns 0  *System.Windows.Controls.DataGridTextColumn  ,  &System.Windows.Controls.DataGridColumn   Header$ 
Space Name��   
IsReadOnly$
 True��1  +System.Windows.Controls.DataGridBoundColumn   Binding �� *Name+  $ Raw Priority�� �� *
Priority+ �� UpdateSourceTrigger$ PropertyChanged=��� 	 �� Orientation$	 
Horizontal=�$��Right=�$��10q�$��1��.+��� -   
 ��Name$
 OkButton��$��OK��$��75��$��5q��� -   $
 CancelButton��$��Cancel��$��75��$��5q�