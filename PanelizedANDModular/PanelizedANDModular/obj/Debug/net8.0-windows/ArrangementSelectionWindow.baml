   M S B A M L   `   `   `  ����  PanelizedAndModularFinal9    3PanelizedAndModularFinal.ArrangementSelectionWindow   5   	   W SPresentationCore, Version=8.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35\ XPresentationFramework, Version=8.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35e aSystem.Windows.Controls.Ribbon, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089R NWindowsBase, Version=8.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35F 9http://schemas.microsoft.com/winfx/2006/xaml/presentation     5   	   R NSystem.Xaml, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089:x,http://schemas.microsoft.com/winfx/2006/xaml     5   	     � Title$  Select Arrangement��5   	   $	��400��5   	   $	��600��6    � 
ResizeMode$ CanResizeWithGrip=�5   	    � WindowStartupLocation$ CenterOwner=�5   	   .��5	      � $��10q�6    � RowDefinitions 5
   
   �� 5      $
��Auto �6   �� 5      $
��Auto �6   �� 5      $
��Auto �6   �� 5      $��* �6   �� 5      $
��Auto �6   .+�5   
   �� $��0��6   $$��Please select one arrangement:��5      $
��Bold"�5      $��14(�5      $��0,0,0,4q�5      �� 5   
   $��1��6   $F��@Legend: 0 = Normal (length→horiz), 1 = Rotated (width→horiz)��5       $��Italic$�5!      $��12(�5"      $��0,0,0,4q�5#      �� 5&   
   -   5         ��Name$ tbModuleCount��5'      $��2��5&      $��Total modules: 0��5(      $��12(�5)      $��0,0,0,8q�5*      �� 5-   
   -   5         ��Name$ lbArrangements��5-      $��3��5.       �� DisplayMemberPath$ DisplayText��5/       �� SelectedIndex$ 0��50      $
��Auto=�51      $��1q�52      $��4q�53      �� 56   
   $��4��6    �� Orientation$ 
Horizontal=�57      $��Right=�58      $��0,10,0,0q�59      .+�5:      �� -   5        	 ��Name$	 btnOK��5:      $��75��5;      $��0,0,10,0q�5<      
 �� 	IsDefault$

 True��5>      .��6    OK5@      �� 5A      -   5        $��75��5A       �� IsCancel$
 True��5C      .��6   Cancel5E      5F      5G      5H      