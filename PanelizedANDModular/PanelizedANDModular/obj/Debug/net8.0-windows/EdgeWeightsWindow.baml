   M S B A M L   `   `   `  ����  PanelizedAndModularFinal0    *PanelizedAndModularFinal.EdgeWeightsWindow   5   	   W SPresentationCore, Version=8.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35\ XPresentationFramework, Version=8.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35e aSystem.Windows.Controls.Ribbon, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089R NWindowsBase, Version=8.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35F 9http://schemas.microsoft.com/winfx/2006/xaml/presentation     5   	   R NSystem.Xaml, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089:x,http://schemas.microsoft.com/winfx/2006/xaml     5   	     � Title$'  !Enter Weights for Connected Rooms��5   	   $	��400��63   $	��500��6@   .��5      �  � RowDefinitions 5   
   �� 5      $
��Auto �6   �� 5      �� 5	      $
��Auto �6   .+�5   
   �� $+��%Enter weight for each connected pair:��6   $��10q�5      $
��Bold"�6    &   System.Windows.Controls.DataGrid  5   
   -   5          Name$ EdgeWeightsGrid��5      $��1��5         AutoGenerateColumns$ False��5      $��10q�5         Columns 5      0  *System.Windows.Controls.DataGridTextColumn  5      ,  &System.Windows.Controls.DataGridColumn   Header$ 
Connection��6%      
IsReadOnly$
 True��6]      Width5  /System.Windows.Controls.DataGridLengthConverter$ * 6o   1  +System.Windows.Controls.DataGridBoundColumn   Binding 69   �� *EdgeDescription+6o     5      $ Weight��6%   $ 80 6�    65   �� *Weight+	 �� Mode$	 TwoWay=�
 �� UpdateSourceTrigger$
 PropertyChanged=�6�   5      �� 5   
   $��2��6    �� Orientation$ 
Horizontal=�6"   $��Right=�6;   $��10q�6W   .+�5      �� -   5        $
��Back��5      $��75��6$   $��5q�6/   6:   �� 5      -   5        $��OK��5      $��70��6"   $��5q�6-   68   �� 5      -   5        $��Cancel��5      $��70��6&   $��5q�61   6<   5      5      5      