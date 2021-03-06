<?php
        $stamp = $_GET["stamp"];
        $ext = $_GET["ext"];
        if (!isset($stamp)) $stamp=time();
        $fname = date("Y-m-d-", $stamp);
        $today = date("Y-m-d-", time());
        if ($fname != $today && file_exists($fname.$ext.".png")) {
                header('Content-type: image/png');      
                readfile($fname.$ext.".png");
                return;
        }
        for ($i = 0; $i < 24; ++$i) {
                if ($i<10) $fn = $fname."0".$i.".$ext"; else $fn = $fname.$i.".$ext";                
                if (file_exists($fn)) {
                        $f = fopen($fn,"r");
                        $t = fread($f, filesize($fn));
                        fclose($f);
                       
                        $lines = split("\n", $t);
                        for ($j =0; $j < count($lines); $j++) {
                                $args = split("\|", $lines[$j]);
                                $mod = $args[0];
                                if (strstr($mod, "Complete Annihilation")!="") $mod = "Complete Annihilation";
                                else if (strstr($mod, "Zero-K")!="") $mod = "Zero-K";
                                else if (strstr($mod, "Balanced Annihilation") != "") $mod = "Balanced Annihilation";
                                else if (strstr($mod, "1944") != "") $mod = "S:1944";
                                else if (strstr($mod, "XTA") != "") $mod = "XTA";
                                else if (strstr($mod, "Gundam") != "") $mod = "Gundam";
                                else if (strstr($mod, "Evolution RTS") != "") $mod = "Evolution RTS";
                                else if (strstr($mod, "NOTA") != "") $mod = "NOTA";
                                else if (strstr($mod, "Kernel Panic") != "") $mod = "Kernel Panic";
                                else if (strstr($mod, "Tired Annihilation") != "") $mod = "Tired Annihilation";
                                else if (strstr($mod, "Expand and Exterminate") != "") $mod = "Expand and Exterminate";
                                else if (strstr($mod, "Tech Annihilation") != "") $mod = "Tech Annihilation";
                                else if (strstr($mod, "LLTA") != "") $mod = "LLTA";
                                else if (strstr($mod, "Absolute Annihilation") != "") $mod = "Absolute Annihilation";
                                else if (strstr($mod, "Star Wars") != "") $mod = "Star Wars";
                                else if (strstr($mod, "COM Shooter") != "") $mod = "COM Shooter";
                                else if (strstr($mod, "KuroTA") != "") $mod = "KuroTA";
                                else if (strstr($mod, "Final Frontier") != "") $mod = "Final Frontier";
                                else if (strstr($mod, "Fibre") != "") $mod = "Fibre";
                                else if (strstr($mod, "Epic Legions") != "") $mod = "Epic Legions";
                                else if (strstr($mod, "Ultimate Annihilation") != "") $mod = "Ultimate Annihilation";
                                else if (strstr($mod, "BA Chicken Defense") != "") $mod = "BA Chicken Defense";
                                else if (strstr($mod, "Spring Tanks") != "") $mod = "Spring Tanks";
                                else if (strstr($mod, "Conflict Terra") != "") $mod = "Conflict Terra";

                                $cnt = $args[1];
                               
                                $a = intval($sums["$mod"]);
                                $b = intval($cnt);
                                $c = $a+$b;
                                $sums["$mod"] = intval($c);
                        }
                }
        }

require("piegraph.class.php");

if (count($sums) <=0) exit("");

$cnt=0;
$ngameswithoutspecificolor=0;
foreach ($sums as $modname => $count) {
                if ($count > 0 && $modname!="") {
                $vals[$cnt] = $count;
                $vars[$cnt] = $modname."($count)";
                if ($modname == "Zero-K") $cols[$cnt]="#00FFFF"; //teal
                elseif ($modname == "Balanced Annihilation") $cols[$cnt]="#008F00"; //green
                elseif ($modname == "Tech Annihilation") $cols[$cnt]="#0070F0";     //blue
                elseif ($modname == "XTA") $cols[$cnt]="#FF0000";                     //red
                elseif ($modname == "NOTA") $cols[$cnt]="#FFFF00";                     //yellow
                elseif ($modname == "S:1944") $cols[$cnt]="#F0E68C";                 //dark yellow/khaki
                elseif ($modname == "BA Chicken Defense") $cols[$cnt]="#708F00";     //dark green
                elseif ($modname == "Gundam") $cols[$cnt]="#FFFFFF";                 //white
                elseif ($modname == "Spring Tanks") $cols[$cnt]="#FF00FF";             //pink
                elseif ($modname == "Kernel Panic") $cols[$cnt]="#2EFE2E";             //bright green
                elseif ($modname == "Evolution RTS") $cols[$cnt]="#F7BE81";         //bright orange-ish
                elseif ($modname == "Conflict Terra") $cols[$cnt]="#B404AE";         //deep purple
                elseif ($modname == "Cursed") $cols[$cnt]="#A80000";                 //dark red                  
                else {
                    $greyshade = intval(220/(($ngameswithoutspecificolor/4)+1)); //growing darkness
                    $cols[$cnt] = "#".dechex ($greyshade).dechex ($greyshade).dechex ($greyshade);
                    $ngameswithoutspecificolor++;
                }
                $cnt++;
                }
}
$pie = new PieGraph(400, 400, $vals);
$pie->setColors($cols);
$pie->setLegends($vars);
$pie->set3dHeight(15);
if ($fname != $today) $pie->display($fname.$ext.".png");
else $pie->display();


?>
